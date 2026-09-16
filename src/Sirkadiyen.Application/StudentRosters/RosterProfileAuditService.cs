using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// Checks a cohort's stored profiles against the published faculty lists, and corrects the ones
/// that disagree — the one-time reconciliation the Grade 3 faculty-practice group needed (ADR-159).
/// </summary>
/// <remarks>
/// It exists because of a specific, dated gap. A dimension the lists did not state at onboarding was
/// entered by the student by hand: the Grade 3 Turkish faculty-practice cohort had no roster column
/// until the faculty published one, so every student who onboarded before it chose their group
/// themselves, and some chose wrong. Now that the column exists (the G3-TR list states it in column
/// E), the authoritative value can be compared with what each student entered and the disagreements
/// put right.
/// <para>
/// Like a rollover (ADR-115) and a cohort repair (ADR-111), this writes no calendar itself. It
/// corrects a profile through the student's own write path — <see cref="StudentProfileService"/>,
/// whose upsert queues the ADR-096 convergence in the same transaction — so the correction inherits
/// the activation guard, the schema validation and the audience/resync a student's own save has,
/// rather than a second path that could drift from them (the ADR-158 rule, applied to a batch).
/// <para>
/// It never guesses. A value is corrected only where a list actually states one for that student;
/// a student the lists do not resolve, or a dimension no readable list states, is reported and left
/// exactly as it is (ADR-085). A list Google could not read this cycle confirms nothing, so a stale
/// list can never drive a "correction".
/// </para>
/// </remarks>
public sealed class RosterProfileAuditService(
    IRosterProfileAuditStore auditStore,
    StudentRosterLookupService lookup,
    StudentProfileService profileService,
    SupportedProfileSchema schema,
    IOperationalFreezeStore freezeStore,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Works out which stored profiles disagree with the lists and what each would become, writing
    /// nothing. Safe to call repeatedly, and the only way an operator sees what they authorize.
    /// </summary>
    public async Task<RosterProfileAuditPlan> PlanAsync(
        RosterProfileAuditScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // The academic year is the deployed schema's, never the caller's. A cohort the schema
        // declares no program for has no profiles to check and no year to name.
        if (schema.FindProgram(scope.ClassYear, scope.ProgramLanguage) is not { } program)
        {
            return EmptyPlan(scope, academicYear: string.Empty, examined: 0);
        }

        IReadOnlyList<StudentProfileView> profiles = await auditStore.ListCohortProfilesAsync(
            program.AcademicYear,
            scope.ClassYear,
            scope.ProgramLanguage,
            cancellationToken);

        List<RosterProfileUserPlan> users = [];
        List<Guid> unresolved = [];
        SortedSet<string> unreadable = new(StringComparer.Ordinal);
        int inAgreement = 0;

        foreach (StudentProfileView profile in profiles.OrderBy(profile => profile.UserId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            StudentRosterLookupResult result = await lookup.LookUpAsync(
                profile.StudentNumber,
                cancellationToken);

            foreach (string rosterId in result.UnreadableRosterIds)
            {
                unreadable.Add(rosterId);
            }

            if (result.Outcome != StudentRosterLookupOutcome.Matched)
            {
                // Not on any list, on two that disagree, or in a program the schema does not
                // onboard: reported for a person, never corrected against a guess (ADR-085).
                unresolved.Add(profile.UserId);
                continue;
            }

            List<RosterProfileCorrection> corrections =
            [
                .. result.SuggestedSelectors
                    .Where(suggested => !string.Equals(
                        profile.Selectors.GetValueOrDefault(suggested.Key),
                        suggested.Value,
                        StringComparison.Ordinal))
                    .Select(suggested => new RosterProfileCorrection
                    {
                        Dimension = suggested.Key,
                        StoredValue = profile.Selectors.GetValueOrDefault(suggested.Key),
                        RosterValue = suggested.Value,
                    })
                    .OrderBy(correction => correction.Dimension, StringComparer.Ordinal),
            ];

            if (corrections.Count == 0)
            {
                inAgreement++;
                continue;
            }

            users.Add(new RosterProfileUserPlan
            {
                UserId = profile.UserId,
                Corrections = corrections,
            });
        }

        return new RosterProfileAuditPlan
        {
            Scope = scope,
            AcademicYear = program.AcademicYear,
            SchemaVersion = schema.SchemaVersion,
            ProfilesExamined = profiles.Count,
            ProfilesInAgreement = inAgreement,
            Users = users,
            TotalCorrections = users.Sum(user => user.Corrections.Count),
            UnresolvedByRoster = [.. unresolved.OrderBy(id => id)],
            UnreadableRosterIds = [.. unreadable],
            PlanHash = ComputePlanHash(
                scope,
                program.AcademicYear,
                schema.SchemaVersion,
                users,
                unresolved,
                unreadable),
        };
    }

    /// <summary>
    /// Applies the corrections the operator confirmed, refusing if the cohort or the live lists
    /// have moved since they saw the plan.
    /// </summary>
    /// <param name="recordAuthorization">
    /// Writes the audit record of the plan being authorized, called immediately before any profile
    /// is rewritten. A throw abandons the run: this rewrites data students entered about themselves
    /// and queues calendar writes no published revision derived, so "why did my cohort change" has
    /// to be answerable from the trail alone (AI_GUIDELINE §19).
    /// </param>
    public async Task<RosterProfileAuditRequestResult> RequestAsync(
        RosterProfileAuditScope scope,
        string confirmedPlanHash,
        Func<RosterProfileAuditPlan, CancellationToken, Task> recordAuthorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmedPlanHash);
        ArgumentNullException.ThrowIfNull(recordAuthorization);

        // Every path that queues calendar work reads the same authoritative switch and fails closed
        // (ADR-034/043): correcting a profile queues the audience convergence, so a freeze that
        // exists to stop calendar writes must stop these too.
        if (await freezeStore.IsFrozenAsync(
                new OperationalFreezeScope
                {
                    ClassYear = scope.ClassYear,
                    ProgramLanguage = scope.ProgramLanguage,
                },
                cancellationToken))
        {
            return new RosterProfileAuditRequestResult
            {
                Outcome = RosterProfileAuditOutcome.Frozen,
            };
        }

        // Replanned rather than trusted from the caller: a confirmation authorizes a plan, and the
        // only way to know it is still that plan — the cohort unchanged and every list stating the
        // same thing — is to compute it again.
        RosterProfileAuditPlan plan = await PlanAsync(scope, cancellationToken);

        if (plan.AcademicYear.Length == 0)
        {
            return new RosterProfileAuditRequestResult
            {
                Outcome = RosterProfileAuditOutcome.NotSupportedBySchema,
                Plan = plan,
                Refusal = $"The deployed supported-profile schema declares no program for class "
                    + $"year {scope.ClassYear} {scope.ProgramLanguage}, so there is nothing to "
                    + "check against the published lists.",
            };
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(plan.PlanHash),
                Encoding.UTF8.GetBytes(confirmedPlanHash)))
        {
            return new RosterProfileAuditRequestResult
            {
                Outcome = RosterProfileAuditOutcome.PlanChanged,
                Plan = plan,
            };
        }

        if (plan.Users.Count == 0)
        {
            return new RosterProfileAuditRequestResult
            {
                Outcome = RosterProfileAuditOutcome.NothingToCorrect,
                Plan = plan,
            };
        }

        // Before the side effect, never after: if this throws, nothing has been rewritten yet and
        // the operator gets an error instead of a silent, unrecorded change to stored profiles.
        await recordAuthorization(plan, cancellationToken);

        // The stored profiles as they are now, keyed by user, so a correction overlays the roster
        // values onto the student's own other selectors rather than replacing the whole profile.
        Dictionary<Guid, StudentProfileView> stored =
            (await auditStore.ListCohortProfilesAsync(
                plan.AcademicYear,
                scope.ClassYear,
                scope.ProgramLanguage,
                cancellationToken))
            .ToDictionary(profile => profile.UserId);

        int corrected = 0;
        int resynced = 0;
        int skipped = 0;

        foreach (RosterProfileUserPlan user in plan.Users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!stored.TryGetValue(user.UserId, out StudentProfileView? profile))
            {
                // The profile vanished between the re-plan and here (a concurrent deletion). It is
                // no longer ours to correct.
                skipped++;
                continue;
            }

            Dictionary<string, string> selectors = new(profile.Selectors, StringComparer.Ordinal);
            foreach (RosterProfileCorrection correction in user.Corrections)
            {
                selectors[correction.Dimension] = correction.RosterValue;
            }

            SaveStudentProfileResult save = await profileService.SaveAsync(
                user.UserId,
                new SubmittedStudentProfile
                {
                    ClassYear = profile.ClassYear,
                    ProgramLanguage = profile.ProgramLanguage,
                    StudentNumber = profile.StudentNumber,
                    Selectors = selectors,
                },
                cancellationToken);

            switch (save.Outcome)
            {
                case SaveStudentProfileOutcome.Saved:
                    corrected++;
                    if (save.CalendarResyncRequested)
                    {
                        resynced++;
                    }

                    break;

                // A suspended account, or one whose other selectors no longer satisfy the schema, is
                // left exactly as it is and counted — the same guard the student's own save has, not
                // a looser path (ADR-158).
                default:
                    skipped++;
                    break;
            }
        }

        return new RosterProfileAuditRequestResult
        {
            Outcome = RosterProfileAuditOutcome.Corrected,
            ProfilesCorrected = corrected,
            CalendarResyncRequested = resynced,
            ProfilesSkipped = skipped,
            Plan = plan,
        };
    }

    private static RosterProfileAuditPlan EmptyPlan(
        RosterProfileAuditScope scope,
        string academicYear,
        int examined) => new()
        {
            Scope = scope,
            AcademicYear = academicYear,
            SchemaVersion = string.Empty,
            ProfilesExamined = examined,
            ProfilesInAgreement = 0,
            Users = [],
            TotalCorrections = 0,
            UnresolvedByRoster = [],
            UnreadableRosterIds = [],
            PlanHash = ComputePlanHash(scope, academicYear, string.Empty, [], [], []),
        };

    /// <summary>
    /// Hashes the plan an operator was shown. The per-user corrections are part of the material, not
    /// only the totals: the same number of corrections over a different set of students, or the same
    /// student corrected to a different value, is a different operation, and confirming one must not
    /// authorize the other (the ADR-107 pattern).
    /// </summary>
    private static string ComputePlanHash(
        RosterProfileAuditScope scope,
        string academicYear,
        string schemaVersion,
        IReadOnlyList<RosterProfileUserPlan> users,
        IReadOnlyCollection<Guid> unresolved,
        IEnumerable<string> unreadable)
    {
        StringBuilder material = new();
        material.Append("roster-profile-audit/v1\n");
        material.Append(academicYear).Append('\n');
        material.Append(schemaVersion).Append('\n');
        material.Append(scope.ClassYear.ToString(CultureInfo.InvariantCulture)).Append('\n');
        material.Append(scope.ProgramLanguage.ToString()).Append('\n');

        foreach (RosterProfileUserPlan user in users.OrderBy(user => user.UserId))
        {
            material.Append(user.UserId.ToString("N")).Append(':');
            foreach (RosterProfileCorrection correction in user.Corrections
                .OrderBy(correction => correction.Dimension, StringComparer.Ordinal))
            {
                material.Append(correction.Dimension)
                    .Append('=')
                    .Append(correction.StoredValue ?? string.Empty)
                    .Append('>')
                    .Append(correction.RosterValue)
                    .Append(';');
            }

            material.Append('\n');
        }

        foreach (Guid userId in unresolved.OrderBy(id => id))
        {
            material.Append("unresolved:").Append(userId.ToString("N")).Append('\n');
        }

        foreach (string rosterId in unreadable.OrderBy(id => id, StringComparer.Ordinal))
        {
            material.Append("unreadable:").Append(rosterId).Append('\n');
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }
}
