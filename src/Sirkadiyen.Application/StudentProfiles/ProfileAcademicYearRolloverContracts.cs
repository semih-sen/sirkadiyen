using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// The program whose stored profiles a rollover moves to the year its sources now state
/// (ADR-115).
/// </summary>
/// <remarks>
/// Only the year being moved <em>from</em> is named. The year moved <em>to</em> is read from the
/// deployed supported-profile schema and never accepted from the caller, because the whole point
/// of the operation is to make stored profiles agree with what a new sign-up would be stamped
/// with. An operator able to type the target year could split one cohort across two years, which
/// is the failure this exists to repair rather than to reproduce.
/// </remarks>
public sealed record ProfileRolloverScope
{
    public required string FromAcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public override string ToString() =>
        $"{FromAcademicYear} year {ClassYear} {ProgramLanguage}";
}

/// <summary>One stored profile as the rollover planner needs to see it.</summary>
public sealed record ProfileRolloverCandidate
{
    public required Guid UserId { get; init; }

    public required StudentProfileView Profile { get; init; }

    /// <summary>
    /// Whether a calendar connection exists that could take a convergence request. A profile
    /// without one is still moved — the year on it must be right whenever the student does
    /// connect — but nothing is queued for it, and the operator is told how many those are.
    /// </summary>
    public required bool HasSyncReadyConnection { get; init; }

    /// <summary>
    /// The lessons this user's calendar currently holds, as (source, stable identity) pairs. An
    /// identity means nothing outside the source that minted it (ADR-096).
    /// </summary>
    public required IReadOnlyList<HeldLessonIdentity> Held { get; init; }
}

/// <summary>One ledger row, reduced to what a rollover plan reasons about.</summary>
public sealed record HeldLessonIdentity
{
    public required string SourceId { get; init; }

    public required string StableIdentity { get; init; }
}

/// <summary>What rolling one student's profile forward would mean for their calendar.</summary>
public sealed record ProfileRolloverUserPlan
{
    public required Guid UserId { get; init; }

    /// <summary>
    /// Lessons published for the target year that resolve to this student and are not on their
    /// calendar. This is the number that was silently zero before the rollover, because the
    /// cohort query filters on the profile's academic year.
    /// </summary>
    public required int GainedEventCount { get; init; }

    /// <summary>
    /// Ledger rows for the year being left whose lesson is no longer published under the target
    /// year. Convergence will not remove them — that would be deleting from absence (ADR-089) —
    /// so they stay on the calendar as last year's history, and the operator is told the number
    /// rather than discovering it from a support request.
    /// </summary>
    public required int StrandedEventCount { get; init; }

    /// <summary>Whether anything will actually be queued for this user.</summary>
    public required bool ConvergenceQueueable { get; init; }
}

/// <summary>The reviewable plan an operator confirms (ADR-115, the ADR-111 pattern).</summary>
public sealed record ProfileRolloverPlan
{
    public required ProfileRolloverScope Scope { get; init; }

    /// <summary>The year the deployed schema states for this program.</summary>
    public required string ToAcademicYear { get; init; }

    /// <summary>The schema version every moved profile will be re-stamped with.</summary>
    public required string ToSchemaVersion { get; init; }

    /// <summary>Profiles that would move, ordered by user id so the hash is deterministic.</summary>
    public required IReadOnlyList<ProfileRolloverUserPlan> Users { get; init; }

    public required int TotalGainedEvents { get; init; }

    public required int TotalStrandedEvents { get; init; }

    /// <summary>
    /// Profiles in scope that would be moved but have no connection able to take a convergence
    /// request. Their year is corrected; nothing is written to a calendar for them.
    /// </summary>
    public required int ProfilesWithoutSyncReadyConnection { get; init; }

    /// <summary>
    /// Profiles in scope whose selectors are not valid under the target year's program, and which
    /// are therefore excluded from the move entirely. A rollover that re-stamped them would leave
    /// a stored profile the schema refuses, so they are reported for a human to resolve
    /// (a re-onboarding, or a schema that still declares their dimension).
    /// </summary>
    public required IReadOnlyList<Guid> BlockedByInvalidSelectors { get; init; }

    /// <summary>
    /// Binds a confirmation to the plan that was displayed rather than to whatever the cohort
    /// resolves to a minute later (the ADR-107 pattern).
    /// </summary>
    public required string PlanHash { get; init; }
}

/// <summary>The outcome of requesting a rollover.</summary>
public sealed record ProfileRolloverRequestResult
{
    public required ProfileRolloverOutcome Outcome { get; init; }

    /// <summary>Profiles re-stamped with the target year; zero for every outcome but success.</summary>
    public int ProfilesMoved { get; init; }

    /// <summary>Connections flagged for the convergence pass that writes the new year's lessons.</summary>
    public int ConvergenceRequested { get; init; }

    public ProfileRolloverPlan? Plan { get; init; }

    /// <summary>Why the request was refused, when <see cref="Outcome"/> says it was.</summary>
    public string? Refusal { get; init; }
}

public enum ProfileRolloverOutcome
{
    /// <summary>Every eligible profile was moved and its connection flagged.</summary>
    Moved,

    /// <summary>The cohort resolved differently from the plan the operator confirmed.</summary>
    PlanChanged,

    /// <summary>No profile in scope needs moving.</summary>
    NothingToMove,

    /// <summary>A freeze is in force, so no calendar work may be queued (ADR-034/043).</summary>
    Frozen,

    /// <summary>
    /// The deployed schema does not support this rollover: it declares no such program, or it
    /// still states the year being moved from. Deploy the schema first.
    /// </summary>
    NotSupportedBySchema,
}

/// <summary>One automatic reconciler pass across every program the schema declares (ADR-117).</summary>
public sealed record ProfileDriftReconcileRunResult
{
    /// <summary>Whether the pass did nothing because the global operational freeze is active.</summary>
    public required bool Frozen { get; init; }

    /// <summary>
    /// Only the programs with something to say. A program in steady state is deliberately absent,
    /// so the worker log stays silent except while a rollover is actually happening.
    /// </summary>
    public required IReadOnlyList<ProfileDriftReconciliation> Programs { get; init; }
}

/// <summary>What the reconciler did, or refused to do, for one program.</summary>
public sealed record ProfileDriftReconciliation
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    /// <summary>The year the deployed schema states for this program.</summary>
    public required string ToAcademicYear { get; init; }

    public required string ToSchemaVersion { get; init; }

    public required ProfileDriftOutcome Outcome { get; init; }

    /// <summary>Profiles found on another year in this pass, bounded by the per-cycle limit.</summary>
    public required int DriftedProfiles { get; init; }

    public required int ProfilesMoved { get; init; }

    /// <summary>Connections flagged for the convergence that writes the new year's lessons.</summary>
    public required int ConvergenceRequested { get; init; }

    /// <summary>
    /// Profiles left exactly as they are because the target program refuses their selectors. They
    /// need a person, and the operator screen is where their owners are named.
    /// </summary>
    public required IReadOnlyList<Guid> BlockedByInvalidSelectors { get; init; }

    public override string ToString() =>
        $"class {ClassYear} {ProgramLanguage} → {ToAcademicYear}";
}

public enum ProfileDriftOutcome
{
    /// <summary>Every profile already states the year the schema does. The steady state.</summary>
    NoDrift,

    /// <summary>Profiles were restamped and their calendars queued for convergence.</summary>
    Moved,

    /// <summary>
    /// Nothing is published for the target year yet, so moving a student onto it would guarantee
    /// them an empty calendar. The reconciler waits for the first revision instead.
    /// </summary>
    NothingPublishedYet,

    /// <summary>
    /// This program is frozen. The scoped freeze is the reconciler's off switch: an operator who
    /// wants to time a rollover by hand freezes the program and uses the screen.
    /// </summary>
    Frozen,

    /// <summary>
    /// Every drifted profile in this batch has selectors the target program refuses, so none could
    /// be moved. It needs a person rather than another cycle.
    /// </summary>
    AllBlocked,
}
