using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>What looking a student number up in the published lists produced.</summary>
/// <remarks>
/// A successful lookup is a suggestion, never an authorization claim and never a
/// statement that the profile is complete (ADR-085). The caller must be able to
/// tell what was suggested from what still needs the student's own answer, which
/// is why <see cref="DimensionsRequiringInput"/> and <see cref="Notices"/> are
/// part of the result rather than something the UI has to work out.
/// </remarks>
public sealed record StudentRosterLookupResult
{
    public required StudentRosterLookupOutcome Outcome { get; init; }

    public required string StudentNumber { get; init; }

    /// <summary>The list the student was found in, when exactly one holds them.</summary>
    public string? RosterId { get; init; }

    /// <summary>
    /// The name the list states, for the student to recognize themselves by.
    /// </summary>
    /// <remarks>
    /// Ephemeral display data. It is returned and then forgotten: never persisted,
    /// never copied into the profile, never logged (ADR-085). Two of the four
    /// lists publish it already masked, so it may arrive as <c>HAY*******</c>.
    /// </remarks>
    public string? GivenName { get; init; }

    public string? FamilyName { get; init; }

    public string? AcademicYear { get; init; }

    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    /// <summary>
    /// The profile values the list states, filtered to what the supported-profile
    /// schema declares for the matched program.
    /// </summary>
    public IReadOnlyDictionary<string, string> SuggestedSelectors { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The required dimensions of the matched program that nothing suggested a
    /// value for, so the student must answer them.
    /// </summary>
    public IReadOnlyList<string> DimensionsRequiringInput { get; init; } = [];

    /// <summary>
    /// Every reason a value the lists hold did not become a suggestion, and every
    /// reason this match cannot carry onboarding on its own.
    /// </summary>
    public IReadOnlyList<StudentRosterLookupNotice> Notices { get; init; } = [];

    /// <summary>The lists that hold this number, when more than one does.</summary>
    public IReadOnlyList<string> ConflictingRosterIds { get; init; } = [];

    /// <summary>
    /// The lists that could not be read at all when this lookup ran.
    /// </summary>
    /// <remarks>
    /// It matters most alongside <see cref="StudentRosterLookupOutcome.NotFound"/>:
    /// "you are not on any list" and "we could not read one of the lists" ask the
    /// student for different things, and the second must not be presented as the
    /// first.
    /// </remarks>
    public IReadOnlyList<string> UnreadableRosterIds { get; init; } = [];
}

public enum StudentRosterLookupOutcome
{
    /// <summary>Exactly one list, and one row of it, states this number.</summary>
    Matched,

    /// <summary>
    /// No configured list states it. Not an error and not a reason to invent
    /// identity: the student enters their profile by hand.
    /// </summary>
    NotFound,

    /// <summary>
    /// More than one row states it, in one list or across two. The backend does
    /// not choose between them (ADR-085); a human resolves it with the faculty.
    /// </summary>
    Ambiguous,
}

public sealed record StudentRosterLookupNotice
{
    public required StudentRosterLookupNoticeCode Code { get; init; }

    public required string Message { get; init; }

    /// <summary>The dimension the notice is about, when it is about one.</summary>
    public string? Dimension { get; init; }
}

public enum StudentRosterLookupNoticeCode
{
    /// <summary>
    /// The student's program is not one the supported-profile schema declares, so
    /// nothing about it can be suggested and onboarding cannot continue into it.
    /// Grade 2 English (ADR-084) and Grade 3 English (ADR-098) are both here.
    /// </summary>
    ProgramNotOnboardable,

    /// <summary>
    /// The program requires this dimension and the list does not state it, so the
    /// student answers it. Grade 2 Turkish anatomy groups and the Grade 3
    /// faculty-practice cohorts are both in this position.
    /// </summary>
    DimensionNotStatedByRoster,

    /// <summary>
    /// The list states a dimension the matched program does not declare, so it is
    /// not suggested. A stale list left over from a different year's structure
    /// looks like this.
    /// </summary>
    DimensionNotDeclaredByProgram,

    /// <summary>
    /// The list states a value the program does not allow for that dimension. The
    /// list may be stale or wrong; either way the value is not offered, because a
    /// suggestion the validator would reject is worse than no suggestion.
    /// </summary>
    ValueNotSupportedByProgram,

    /// <summary>
    /// The list is catalogued for a different academic year than the program its
    /// students onboard into, so nothing it states is suggested.
    /// </summary>
    /// <remarks>
    /// The two years are one fact stated twice and must move together; the one
    /// time they did not, every Grade 2 Turkish calendar silently stopped
    /// receiving lessons (ADR-115). A roster left behind at a rollover would
    /// suggest last year's cohorts into this year's profile, which is the same
    /// failure wearing different clothes.
    /// </remarks>
    RosterYearDiffersFromProgram,
}
