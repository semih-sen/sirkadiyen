using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Plans and requests the explicit, audited repair of a cohort's calendars (ADR-111,
/// AI_GUIDELINE §13).
/// </summary>
/// <remarks>
/// It exists because of a specific gap. When an audience rule is corrected, the lessons it wrongly
/// wrote stay written: the canonical records never changed, so no semantic diff mentions them, and
/// the periodic inventory pass repairs missing and stale events but deliberately never deletes from
/// absence (ADR-089). The only existing path that removes a no-longer-applicable event is the
/// profile-change convergence — and it runs only when a student happens to re-save their profile.
/// Waiting for that is not a cleanup strategy.
/// <para>
/// So this service does not delete anything itself. It computes exactly what is surplus, shows the
/// operator that plan, and on confirmation flags the affected connections for the convergence pass
/// that already exists. Every deletion is therefore still made by
/// <see cref="ProfileChangeResyncService"/>, under its bounds: publication-gated, budgeted per
/// cycle, freeze-aware, resumable, and skipping a user whose credential has died. Adding a second
/// deletion path would mean a second set of those guarantees to keep true.
/// </para>
/// </remarks>
public sealed class CohortCalendarRepairService(
    ICohortCalendarRepairStore repairStore,
    ICanonicalScheduleReadStore scheduleReadStore,
    IOperationalFreezeStore freezeStore,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Works out what a repair would converge, writing nothing. Safe to call repeatedly, and the
    /// only way an operator sees what they are about to authorize.
    /// </summary>
    public async Task<CohortRepairPlan> PlanAsync(
        CohortRepairScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IReadOnlyList<CohortRepairHolding> holdings =
            await repairStore.ListCohortHoldingsAsync(scope, cancellationToken);

        IReadOnlyList<CanonicalScheduleRecord> published =
            await scheduleReadStore.ListCurrentPublishedRecordsAsync(
                scope.AcademicYear,
                scope.ClassYear,
                scope.ProgramLanguage,
                cancellationToken);

        // A ledger row is keyed by (source, identity) and so is published truth, because an
        // identity means nothing outside the source that minted it (ADR-096).
        IReadOnlyList<PublishedRecordIdentity> live =
            await scheduleReadStore.ListCurrentPublishedIdentitiesAsync(
                scope.AcademicYear,
                cancellationToken);
        HashSet<(string Source, string Identity)> liveIdentities =
            [.. live.Select(identity => (identity.SourceId.Value, identity.StableIdentity))];

        List<CohortRepairUserPlan> users = [];
        int cohortRetired = 0;
        foreach (CohortRepairHolding holding in holdings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // What this user's calendar should hold, under the audience rule as it stands now.
            HashSet<string> applicable =
            [
                .. published
                    .Where(record => CalendarAudienceResolver.Applies(record, holding.Profile))
                    .Select(record => record.StableIdentity),
            ];

            int surplus = 0;
            int retired = 0;
            foreach (CalendarEventMappingView mapping in holding.Mappings)
            {
                if (applicable.Contains(mapping.StableIdentity))
                {
                    continue;
                }

                // Still published, and no longer theirs: a decision was published and the
                // calendar disagrees with it. This is the surplus a repair exists to remove.
                if (liveIdentities.Contains((mapping.SourceId.Value, mapping.StableIdentity)))
                {
                    surplus++;
                }
                else
                {
                    // No longer published at all. Removing it would be deleting from absence, so
                    // it is counted for the operator and left alone (ADR-089).
                    retired++;
                }
            }

            HashSet<string> held =
                [.. holding.Mappings.Select(mapping => mapping.StableIdentity)];
            int missing = applicable.Count(identity => !held.Contains(identity));

            // Counted for the whole cohort, not only for the users this pass would act on: a
            // student whose sole anomaly is an unpublished leftover has nothing to converge, and
            // an operator who never sees those rows cannot know they are there to investigate.
            cohortRetired += retired;

            if (surplus == 0 && missing == 0)
            {
                continue;
            }

            users.Add(new CohortRepairUserPlan
            {
                UserId = holding.UserId,
                SurplusEventCount = surplus,
                MissingEventCount = missing,
                UntouchableRetiredCount = retired,
            });
        }

        return new CohortRepairPlan
        {
            Scope = scope,
            Users = users,
            CohortUserCount = holdings.Count,
            TotalSurplusEvents = users.Sum(user => user.SurplusEventCount),
            TotalMissingEvents = users.Sum(user => user.MissingEventCount),
            TotalUntouchableRetired = cohortRetired,
            PlanHash = ComputePlanHash(scope, users, cohortRetired),
        };
    }

    /// <summary>
    /// Requests the repair the operator confirmed, refusing if the cohort has moved since they
    /// saw it.
    /// </summary>
    public async Task<CohortRepairRequestResult> RequestAsync(
        CohortRepairScope scope,
        string confirmedPlanHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmedPlanHash);

        // Every calendar-touching path reads the same authoritative switch and fails closed
        // (ADR-034/043). Queueing work a freeze exists to prevent would defer the writes rather
        // than decline them, which is not what an operator freezing a program asked for.
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
            return new CohortRepairRequestResult { Outcome = CohortRepairOutcome.Frozen };
        }

        // Replanned rather than trusted from the caller: the confirmation authorizes a plan, and
        // the only way to know it is still that plan is to compute it again.
        CohortRepairPlan plan = await PlanAsync(scope, cancellationToken);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(plan.PlanHash),
                Encoding.UTF8.GetBytes(confirmedPlanHash)))
        {
            return new CohortRepairRequestResult
            {
                Outcome = CohortRepairOutcome.PlanChanged,
                Plan = plan,
            };
        }

        if (plan.Users.Count == 0)
        {
            return new CohortRepairRequestResult
            {
                Outcome = CohortRepairOutcome.NothingToRepair,
                Plan = plan,
            };
        }

        int requested = await repairStore.RequestConvergenceAsync(
            [.. plan.Users.Select(user => user.UserId)],
            timeProvider.GetUtcNow(),
            cancellationToken);

        return new CohortRepairRequestResult
        {
            Outcome = CohortRepairOutcome.Requested,
            UsersRequested = requested,
            Plan = plan,
        };
    }

    /// <summary>
    /// Hashes the plan an operator was shown. The per-user counts are part of the material, not
    /// only the totals: the same total surplus spread over a different set of students is a
    /// different repair, and confirming one must not authorize the other (ADR-107's pattern).
    /// </summary>
    private static string ComputePlanHash(
        CohortRepairScope scope,
        IReadOnlyList<CohortRepairUserPlan> users,
        int cohortRetired)
    {
        StringBuilder material = new();
        material.Append("cohort-calendar-repair/v1\n");
        material.Append(scope.AcademicYear).Append('\n');
        material.Append(scope.ClassYear.ToString(CultureInfo.InvariantCulture)).Append('\n');
        material.Append(scope.ProgramLanguage.ToString()).Append('\n');

        foreach (CohortRepairUserPlan user in users.OrderBy(user => user.UserId))
        {
            material.Append(user.UserId.ToString("N"))
                .Append(':')
                .Append(user.SurplusEventCount.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(user.MissingEventCount.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(user.UntouchableRetiredCount.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        material.Append("retired:")
            .Append(cohortRetired.ToString(CultureInfo.InvariantCulture))
            .Append('\n');

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }
}
