using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// The configured faculty student lists ADR-085 looks a student number up in.
/// </summary>
/// <remarks>
/// This is a separate catalog from <c>schedule-sources.json</c> on purpose. A
/// roster is not a schedule source: nothing parses it into canonical records,
/// nothing publishes it to a calendar, and no revision is ever cut from it. It
/// shares the acquisition transports and the normalized snapshot shape, and
/// nothing else.
/// </remarks>
public sealed record StudentRosterCatalog
{
    public required string CatalogVersion { get; init; }

    public IReadOnlyList<StudentRosterDefinition> Rosters { get; init; } = [];
}

/// <summary>One published student list, and how to read it.</summary>
public sealed record StudentRosterDefinition
{
    public required string RosterId { get; init; }

    public required string DisplayName { get; init; }

    public required ScheduleSourceTransport Transport { get; init; }

    public required ScheduleDocumentFormat DocumentFormat { get; init; }

    public required Uri SourceUri { get; init; }

    public string? ExternalId { get; init; }

    public long? SheetGid { get; init; }

    /// <summary>
    /// The cohort this list enrols, which the document itself never states
    /// (ADR-017's rule, applied to rosters). A match therefore tells the caller
    /// which program the student is in; the student is not asked first.
    /// </summary>
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required StudentRosterLayout Layout { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Where the columns are and what each one states.
/// </summary>
/// <remarks>
/// Columns are addressed by header text rather than by index because the four
/// published lists disagree about order: the Grade 2 lists put the student
/// number in the third column and the Grade 3 lists in the first. Header text is
/// matched after trimming and collapsing internal whitespace runs, because one
/// list writes <c>Ad </c> with a trailing space and another writes
/// <c>Genel  Alt Grup</c> with two.
/// </remarks>
public sealed record StudentRosterLayout
{
    /// <summary>The worksheet to read. A roster document holds exactly one.</summary>
    public required string WorksheetTitle { get; init; }

    /// <summary>The one-based row the headers are written on.</summary>
    public required int HeaderRow { get; init; }

    public required string StudentNumberHeader { get; init; }

    public required string GivenNameHeader { get; init; }

    public required string FamilyNameHeader { get; init; }

    /// <summary>
    /// The selector dimensions this list states. A list that states none — the
    /// Grade 3 English one does not — declares an empty collection, which is a
    /// statement that it suggests identity only, not an omission.
    /// </summary>
    public IReadOnlyList<StudentRosterDimensionColumn> DimensionColumns { get; init; } = [];
}

/// <summary>One column of a roster that states a profile selector.</summary>
public sealed record StudentRosterDimensionColumn
{
    public required string Header { get; init; }

    /// <summary>The supported-profile dimension key this column states.</summary>
    public required string Dimension { get; init; }

    /// <summary>
    /// Every value the column is allowed to write, mapped onto the profile value
    /// it means.
    /// </summary>
    /// <remarks>
    /// The map is exhaustive and a value outside it is refused, never
    /// transformed. Case folding would be the obvious shortcut — the Grade 2
    /// Turkish list writes <c>a1</c> where the schema says <c>A1</c> — and it is
    /// the wrong one here. Turkish upper-casing maps <c>i</c> onto <c>İ</c>, so
    /// a rule that reads <c>a1</c> correctly would silently invent an <c>İ1</c>
    /// out of an English list's <c>i1</c>, and those are different cohorts in
    /// different dimensions (ADR-085, and the same trap ADR-130 had to bound).
    /// </remarks>
    public required IReadOnlyDictionary<string, string> ValueMap { get; init; }

    /// <summary>
    /// Whether the column states its value once for a run of students and leaves
    /// the rest of the run's cells empty, merged into the first.
    /// </summary>
    /// <remarks>
    /// All three grouped lists are written this way. An empty cell inside a
    /// declared merged range takes the range's value; an empty cell outside one
    /// is refused rather than carried down from the row above, because a gap the
    /// document did not merge is a gap the document did not state.
    /// </remarks>
    public bool StatedOncePerMergedRun { get; init; }
}
