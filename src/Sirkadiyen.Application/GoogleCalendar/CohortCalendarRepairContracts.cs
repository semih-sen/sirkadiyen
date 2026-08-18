using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>The program whose calendars a repair pass is scoped to (ADR-111).</summary>
/// <remarks>
/// A repair is always scoped to one program. A pass that could run unscoped would be a
/// whole-population calendar operation authorized by one click, which is exactly what
/// AI_GUIDELINE §13 refuses to let become ordinary.
/// </remarks>
public sealed record CohortRepairScope
{
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public override string ToString() =>
        $"{AcademicYear} year {ClassYear} {ProgramLanguage}";
}

/// <summary>One user's ledger holdings, as the repair planner needs to see them.</summary>
public sealed record CohortRepairHolding
{
    public required Guid UserId { get; init; }

    public required StudentProfileView Profile { get; init; }

    /// <summary>Every lesson currently on this user's calendar, per the ledger.</summary>
    public required IReadOnlyList<CalendarEventMappingView> Mappings { get; init; }
}

/// <summary>What a repair pass would change for one user.</summary>
public sealed record CohortRepairUserPlan
{
    public required Guid UserId { get; init; }

    /// <summary>
    /// Events the user holds that are still published but no longer resolve to their profile —
    /// the surplus a resolver bug wrote and no background job will ever remove.
    /// </summary>
    public required int SurplusEventCount { get; init; }

    /// <summary>
    /// Events that apply to the user and are not on their calendar. Convergence writes these too;
    /// they are shown because the operator is authorizing both halves, not only the deletions.
    /// </summary>
    public required int MissingEventCount { get; init; }

    /// <summary>
    /// Ledger rows whose lesson is no longer published at all. They are counted and never touched:
    /// removing one would be deleting from absence rather than from a published decision
    /// (AI_GUIDELINE §13, ADR-089). Retiring them stays the semantic diff's job.
    /// </summary>
    public required int UntouchableRetiredCount { get; init; }
}

/// <summary>The full, reviewable plan an operator confirms (ADR-111).</summary>
public sealed record CohortRepairPlan
{
    public required CohortRepairScope Scope { get; init; }

    /// <summary>Users with something to converge, ordered by id so the hash is deterministic.</summary>
    public required IReadOnlyList<CohortRepairUserPlan> Users { get; init; }

    /// <summary>Every synchronization-ready user in scope, including those needing nothing.</summary>
    public required int CohortUserCount { get; init; }

    public required int TotalSurplusEvents { get; init; }

    public required int TotalMissingEvents { get; init; }

    /// <summary>
    /// Ledger rows across the whole cohort whose lesson is no longer published. Deliberately not
    /// the sum of <see cref="Users"/>: a student whose only anomaly is such a leftover has nothing
    /// to converge and so is absent from that list, but the operator still needs to know the rows
    /// exist. Nothing ever deletes them here (ADR-089).
    /// </summary>
    public required int TotalUntouchableRetired { get; init; }

    /// <summary>
    /// Binds a confirmation to the plan that was displayed, not to whatever the cohort resolves
    /// to a minute later (the ADR-107 pattern).
    /// </summary>
    public required string PlanHash { get; init; }
}

/// <summary>The outcome of requesting a repair.</summary>
public sealed record CohortRepairRequestResult
{
    public required CohortRepairOutcome Outcome { get; init; }

    /// <summary>Connections flagged for convergence; zero for every outcome but success.</summary>
    public int UsersRequested { get; init; }

    public CohortRepairPlan? Plan { get; init; }
}

public enum CohortRepairOutcome
{
    /// <summary>Every affected connection was flagged; the worker converges them.</summary>
    Requested,

    /// <summary>The cohort resolved differently from the plan the operator confirmed.</summary>
    PlanChanged,

    /// <summary>The plan found nothing to converge, so nothing was flagged.</summary>
    NothingToRepair,

    /// <summary>A freeze is in force, so no calendar work may be queued (ADR-034/043).</summary>
    Frozen,
}
