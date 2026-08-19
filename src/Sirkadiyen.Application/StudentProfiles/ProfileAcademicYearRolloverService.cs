using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// Plans and requests the explicit, audited move of a program's stored profiles onto the academic
/// year its sources now state (ADR-115, AI_GUIDELINE §13).
/// </summary>
/// <remarks>
/// It exists because of a specific gap, and a costly one. A profile is stamped with its program's
/// academic year once, when the student saves it, and nothing ever restamps it. When the catalog
/// moves a cohort's sources to a new year, the two halves of incremental dispatch stop agreeing:
/// deletions are driven from the mapping ledger, which never asks about a year, so last year's
/// lessons are removed; insertions are driven from
/// <c>ICalendarSyncTargetReadStore.ListCohortTargetsAsync</c>, which filters profiles on the
/// record's year, so the new year's lessons resolve to nobody. The result is a cohort of emptied
/// calendars while the revision publishes, the diff dispatches and every check downstream reports
/// success.
/// <para>
/// Like a cohort calendar repair (ADR-111), this writes no calendar itself. It corrects the stored
/// year and flags each owner's connection for the convergence pass that already exists, so every
/// event is still written by <see cref="ProfileChangeResyncService"/> under its bounds:
/// publication-gated, budgeted per cycle, freeze-aware, resumable, and never deleting from
/// absence. Adding a second write path would mean a second set of those guarantees to keep true.
/// </para>
/// </remarks>
public sealed class ProfileAcademicYearRolloverService(
    IProfileAcademicYearRolloverStore rolloverStore,
    ICanonicalScheduleReadStore scheduleReadStore,
    SupportedProfileSchema schema,
    IOperationalFreezeStore freezeStore,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Works out what a rollover would move and what each student's calendar would gain, writing
    /// nothing. Safe to call repeatedly, and the only way an operator sees what they authorize.
    /// </summary>
    public async Task<ProfileRolloverPlan> PlanAsync(
        ProfileRolloverScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // The target year is the deployed schema's, never the caller's. A rollover exists to make
        // stored profiles agree with what a new sign-up is stamped with, so if the schema has not
        // been deployed yet there is no year to move to and the answer is to deploy it.
        if (schema.FindProgram(scope.ClassYear, scope.ProgramLanguage) is not { } program)
        {
            return EmptyPlan(scope, toAcademicYear: null);
        }

        if (string.Equals(program.AcademicYear, scope.FromAcademicYear, StringComparison.Ordinal))
        {
            // The schema still states the year being left. Moving profiles onto it would be a
            // no-op; moving them anywhere else would be inventing a year.
            return EmptyPlan(scope, program.AcademicYear);
        }

        IReadOnlyList<ProfileRolloverCandidate> candidates =
            await rolloverStore.ListCandidatesAsync(scope, cancellationToken);
        if (candidates.Count == 0)
        {
            return EmptyPlan(scope, program.AcademicYear);
        }

        IReadOnlyList<CanonicalScheduleRecord> published =
            await scheduleReadStore.ListCurrentPublishedRecordsAsync(
                program.AcademicYear,
                scope.ClassYear,
                scope.ProgramLanguage,
                cancellationToken);

        // Live under the *target* year, because that is the year the profile will carry and so the
        // year convergence will measure its removals against (ADR-089).
        IReadOnlyList<PublishedRecordIdentity> live =
            await scheduleReadStore.ListCurrentPublishedIdentitiesAsync(
                program.AcademicYear,
                cancellationToken);
        HashSet<(string Source, string Identity)> liveIdentities =
            [.. live.Select(identity => (identity.SourceId.Value, identity.StableIdentity))];

        List<ProfileRolloverUserPlan> users = [];
        List<Guid> blocked = [];
        int withoutConnection = 0;

        foreach (ProfileRolloverCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A stored profile is re-validated against the schema it is being moved onto, not
            // trusted because it was valid when it was written. A dimension the new year's
            // program dropped or renamed would otherwise be re-stamped into a profile the schema
            // refuses, and the student would find their own settings page rejecting a profile
            // they never changed.
            if (!SelectorsRemainValid(candidate, program))
            {
                blocked.Add(candidate.UserId);
                continue;
            }

            // The profile as it will read after the move: the same cohort, a different year.
            StudentProfileView moved = candidate.Profile with
            {
                AcademicYear = program.AcademicYear,
            };

            HashSet<string> held =
                [.. candidate.Held.Select(identity => identity.StableIdentity)];

            int gained = published.Count(record =>
                CalendarAudienceResolver.Applies(record, moved)
                && !held.Contains(record.StableIdentity));

            int stranded = candidate.Held.Count(identity =>
                !liveIdentities.Contains((identity.SourceId, identity.StableIdentity)));

            if (!candidate.HasSyncReadyConnection)
            {
                withoutConnection++;
            }

            users.Add(new ProfileRolloverUserPlan
            {
                UserId = candidate.UserId,
                GainedEventCount = gained,
                StrandedEventCount = stranded,
                ConvergenceQueueable = candidate.HasSyncReadyConnection,
            });
        }

        return new ProfileRolloverPlan
        {
            Scope = scope,
            ToAcademicYear = program.AcademicYear,
            ToSchemaVersion = schema.SchemaVersion,
            Users = users,
            TotalGainedEvents = users.Sum(user => user.GainedEventCount),
            TotalStrandedEvents = users.Sum(user => user.StrandedEventCount),
            ProfilesWithoutSyncReadyConnection = withoutConnection,
            BlockedByInvalidSelectors = blocked,
            PlanHash = ComputePlanHash(scope, program.AcademicYear, schema.SchemaVersion, users, blocked),
        };
    }

    /// <summary>
    /// Applies the rollover the operator confirmed, refusing if the cohort has moved since they
    /// saw it.
    /// </summary>
    /// <param name="recordAuthorization">
    /// Writes the audit record of the plan being authorized. It is called immediately before any
    /// profile is rewritten, and a throw from it abandons the rollover: this restamps stored
    /// student data and queues calendar writes that no published revision derived, so "why did
    /// my profile change year" has to be answerable from the trail alone (AI_GUIDELINE §19).
    /// </param>
    public async Task<ProfileRolloverRequestResult> RequestAsync(
        ProfileRolloverScope scope,
        string confirmedPlanHash,
        Func<ProfileRolloverPlan, CancellationToken, Task> recordAuthorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmedPlanHash);
        ArgumentNullException.ThrowIfNull(recordAuthorization);

        // Every path that queues calendar work reads the same authoritative switch and fails
        // closed (ADR-034/043). Queueing work a freeze exists to prevent would defer the writes
        // rather than decline them, which is not what an operator freezing a program asked for.
        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen
            || await freezeStore.IsFrozenAsync(
                new OperationalFreezeScope
                {
                    ClassYear = scope.ClassYear,
                    ProgramLanguage = scope.ProgramLanguage,
                },
                cancellationToken))
        {
            return new ProfileRolloverRequestResult { Outcome = ProfileRolloverOutcome.Frozen };
        }

        // Replanned rather than trusted from the caller: a confirmation authorizes a plan, and the
        // only way to know it is still that plan is to compute it again.
        ProfileRolloverPlan plan = await PlanAsync(scope, cancellationToken);

        if (plan.ToAcademicYear.Length == 0)
        {
            return new ProfileRolloverRequestResult
            {
                Outcome = ProfileRolloverOutcome.NotSupportedBySchema,
                Plan = plan,
                Refusal = $"The deployed supported-profile schema declares no program for class "
                    + $"year {scope.ClassYear} {scope.ProgramLanguage}, or still states "
                    + $"{scope.FromAcademicYear} for it. Deploy the schema that names the new "
                    + $"year before moving any profile onto it.",
            };
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(plan.PlanHash),
                Encoding.UTF8.GetBytes(confirmedPlanHash)))
        {
            return new ProfileRolloverRequestResult
            {
                Outcome = ProfileRolloverOutcome.PlanChanged,
                Plan = plan,
            };
        }

        if (plan.Users.Count == 0)
        {
            return new ProfileRolloverRequestResult
            {
                Outcome = ProfileRolloverOutcome.NothingToMove,
                Plan = plan,
            };
        }

        // Before the side effect, never after: if this throws, nothing has been rewritten yet and
        // the operator gets an error instead of a silent, unrecorded change to stored profiles.
        await recordAuthorization(plan, cancellationToken);

        ProfileRolloverApplyResult applied = await rolloverStore.ApplyAsync(
            [.. plan.Users.Select(user => user.UserId)],
            plan.ToAcademicYear,
            plan.ToSchemaVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return new ProfileRolloverRequestResult
        {
            Outcome = ProfileRolloverOutcome.Moved,
            ProfilesMoved = applied.ProfilesMoved,
            ConvergenceRequested = applied.ConvergenceRequested,
            Plan = plan,
        };
    }

    private static bool SelectorsRemainValid(
        ProfileRolloverCandidate candidate,
        SupportedProfileProgram program) =>
        StudentProfileValidator.Validate(
            new SupportedProfileSchema
            {
                // A one-program schema, so validation answers exactly "do these selectors satisfy
                // the program being moved onto" and cannot accidentally match another one.
                AcademicYear = program.AcademicYear,
                SchemaVersion = string.Empty,
                Programs = [program],
            },
            new SubmittedStudentProfile
            {
                ClassYear = candidate.Profile.ClassYear,
                ProgramLanguage = candidate.Profile.ProgramLanguage,
                StudentNumber = candidate.Profile.StudentNumber,
                Selectors = candidate.Profile.Selectors,
            })
        .IsValid;

    private static ProfileRolloverPlan EmptyPlan(
        ProfileRolloverScope scope,
        string? toAcademicYear) => new()
        {
            Scope = scope,
            // An empty target year is what RequestAsync reads as "the schema does not support
            // this", so the refusal states a cause rather than reporting nothing to do.
            ToAcademicYear = toAcademicYear is null
                || string.Equals(toAcademicYear, scope.FromAcademicYear, StringComparison.Ordinal)
                    ? string.Empty
                    : toAcademicYear,
            ToSchemaVersion = string.Empty,
            Users = [],
            TotalGainedEvents = 0,
            TotalStrandedEvents = 0,
            ProfilesWithoutSyncReadyConnection = 0,
            BlockedByInvalidSelectors = [],
            PlanHash = ComputePlanHash(scope, toAcademicYear ?? string.Empty, string.Empty, [], []),
        };

    /// <summary>
    /// Hashes the plan an operator was shown. The per-user counts are part of the material, not
    /// only the totals: the same total spread over a different set of students is a different
    /// rollover, and confirming one must not authorize the other (the ADR-107 pattern).
    /// </summary>
    private static string ComputePlanHash(
        ProfileRolloverScope scope,
        string toAcademicYear,
        string toSchemaVersion,
        IReadOnlyList<ProfileRolloverUserPlan> users,
        IReadOnlyList<Guid> blocked)
    {
        StringBuilder material = new();
        material.Append("profile-academic-year-rollover/v1\n");
        material.Append(scope.FromAcademicYear).Append('\n');
        material.Append(toAcademicYear).Append('\n');
        material.Append(toSchemaVersion).Append('\n');
        material.Append(scope.ClassYear.ToString(CultureInfo.InvariantCulture)).Append('\n');
        material.Append(scope.ProgramLanguage.ToString()).Append('\n');

        foreach (ProfileRolloverUserPlan user in users.OrderBy(user => user.UserId))
        {
            material.Append(user.UserId.ToString("N"))
                .Append(':')
                .Append(user.GainedEventCount.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(user.StrandedEventCount.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(user.ConvergenceQueueable ? '1' : '0')
                .Append('\n');
        }

        foreach (Guid userId in blocked.OrderBy(id => id))
        {
            material.Append("blocked:").Append(userId.ToString("N")).Append('\n');
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }
}
