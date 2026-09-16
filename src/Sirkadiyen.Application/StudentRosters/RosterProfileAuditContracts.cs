using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// The cohort whose stored profiles are checked against — and corrected to — what the published
/// faculty lists now state (ADR-159).
/// </summary>
/// <remarks>
/// A scope names a program, never a single roster. A Grade 3 Turkish student is described by two
/// complementary lists that the lookup merges (ADR-145), and the correction is only meaningful
/// against everything they jointly state. The academic year is the deployed schema's for the
/// program, never the caller's: the check exists to make stored profiles agree with what a new
/// sign-up would be filled in with, and a caller-supplied year could name a cohort the schema does
/// not describe.
/// </remarks>
public sealed record RosterProfileAuditScope
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public override string ToString() => $"year {ClassYear} {ProgramLanguage}";
}

/// <summary>One dimension a stored profile disagrees with the roster on.</summary>
public sealed record RosterProfileCorrection
{
    public required string Dimension { get; init; }

    /// <summary>
    /// What the student entered, or <see langword="null"/> when they stated nothing for this
    /// dimension — the case that arises when the roster began stating a selector the student had to
    /// choose by hand before (the faculty-practice group is exactly this, ADR-159).
    /// </summary>
    public string? StoredValue { get; init; }

    /// <summary>The value the published list now states, already validated against the program.</summary>
    public required string RosterValue { get; init; }
}

/// <summary>One profile the audit would correct, and to what.</summary>
public sealed record RosterProfileUserPlan
{
    public required Guid UserId { get; init; }

    /// <summary>The disagreeing dimensions, ordered by key so the plan hash is deterministic.</summary>
    public required IReadOnlyList<RosterProfileCorrection> Corrections { get; init; }
}

/// <summary>
/// The reviewable result of checking a cohort's stored profiles against the published lists
/// (ADR-159, the ADR-115 pattern).
/// </summary>
public sealed record RosterProfileAuditPlan
{
    public required RosterProfileAuditScope Scope { get; init; }

    /// <summary>
    /// The year the deployed schema states for this program, or an empty string when the schema
    /// declares no program for the scope — which is how <see cref="RosterProfileAuditService"/>
    /// reports "nothing to check here" without inventing a cohort.
    /// </summary>
    public required string AcademicYear { get; init; }

    /// <summary>The schema version every corrected profile is re-stamped with.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Stored profiles in the cohort the check looked at.</summary>
    public required int ProfilesExamined { get; init; }

    /// <summary>
    /// Profiles the roster resolved and every value it states already agrees with — the students
    /// who entered their cohort correctly, which is the reassuring half of "did the old users get
    /// it right".
    /// </summary>
    public required int ProfilesInAgreement { get; init; }

    /// <summary>The profiles that would change, ordered by user id so the hash is stable.</summary>
    public required IReadOnlyList<RosterProfileUserPlan> Users { get; init; }

    public required int TotalCorrections { get; init; }

    /// <summary>
    /// Profiles whose student number the lists do not resolve to one student — not on any list,
    /// on two that disagree, or in a program the schema does not onboard. They are reported for a
    /// person to look at, never corrected against a guess (ADR-085).
    /// </summary>
    public required IReadOnlyList<Guid> UnresolvedByRoster { get; init; }

    /// <summary>
    /// Lists that could not be read this cycle. A value one of them would have confirmed is left
    /// exactly as the student entered it rather than "corrected" against a document Google was
    /// unreachable for, so the operator is told which lists were stale when they read the plan.
    /// </summary>
    public required IReadOnlyList<string> UnreadableRosterIds { get; init; }

    /// <summary>
    /// Binds a confirmation to the plan that was displayed rather than to whatever the cohort and
    /// the live lists resolve to a minute later (the ADR-107 pattern).
    /// </summary>
    public required string PlanHash { get; init; }
}

/// <summary>The outcome of requesting the corrections a plan describes.</summary>
public sealed record RosterProfileAuditRequestResult
{
    public required RosterProfileAuditOutcome Outcome { get; init; }

    /// <summary>Profiles actually rewritten; zero for every outcome but success.</summary>
    public int ProfilesCorrected { get; init; }

    /// <summary>Corrected profiles whose audience changed and whose calendar was queued to converge.</summary>
    public int CalendarResyncRequested { get; init; }

    /// <summary>
    /// Profiles in the plan that were not rewritten because the account is no longer active, or its
    /// other selectors no longer satisfy the schema. They are left untouched and reported rather
    /// than forced, matching the student's own save guard (ADR-158).
    /// </summary>
    public int ProfilesSkipped { get; init; }

    public RosterProfileAuditPlan? Plan { get; init; }

    /// <summary>Why the request was refused, when <see cref="Outcome"/> says it was.</summary>
    public string? Refusal { get; init; }
}

public enum RosterProfileAuditOutcome
{
    /// <summary>Every eligible profile in the plan was corrected.</summary>
    Corrected,

    /// <summary>The cohort or the live lists resolved differently from the confirmed plan.</summary>
    PlanChanged,

    /// <summary>No profile in scope disagrees with the lists.</summary>
    NothingToCorrect,

    /// <summary>A freeze is in force, so no calendar work may be queued (ADR-034/043).</summary>
    Frozen,

    /// <summary>The deployed schema declares no program for this scope, so there is nothing to check.</summary>
    NotSupportedBySchema,
}
