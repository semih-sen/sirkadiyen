using Sirkadiyen.Contracts.Spreadsheets;

namespace Sirkadiyen.Contracts.Parsing;

public static class ParserContractVersions
{
    public const string V1 = "1.0";
}

public sealed record ParseSnapshotRequest
{
    public required string ContractVersion { get; init; }

    public required string CorrelationId { get; init; }

    public required ParserProfileDescriptor ParserProfile { get; init; }

    public required ParseSourceContext SourceContext { get; init; }

    public required NormalizedSpreadsheetSnapshot Snapshot { get; init; }
}

/// <summary>
/// Facts about the source that the spreadsheet itself does not state. These come
/// from source configuration so the parser never infers them (ADR-017).
/// </summary>
public sealed record ParseSourceContext
{
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string TimeZoneId { get; init; }
}

public sealed record ParseSnapshotResponse
{
    public required string ContractVersion { get; init; }

    public required string CorrelationId { get; init; }

    public required string SourceId { get; init; }

    public required string SnapshotId { get; init; }

    public required ParserProfileDescriptor ParserProfile { get; init; }

    public required ParserResultStatus Status { get; init; }

    public IReadOnlyList<CanonicalScheduleCandidate> Candidates { get; init; } = [];

    public IReadOnlyList<ParserWarning> Warnings { get; init; } = [];

    public IReadOnlyList<ParserMetric> Metrics { get; init; } = [];

    public IReadOnlyList<ConfidenceIndicator> ConfidenceIndicators { get; init; } = [];
}

public sealed record ParserProfileDescriptor
{
    public required string Name { get; init; }

    public required string Version { get; init; }
}

public enum ParserResultStatus
{
    Completed,
    CompletedWithWarnings,
    Rejected,
}

public sealed record CanonicalScheduleCandidate
{
    public required string CandidateId { get; init; }

    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required ScheduleAudienceCandidate Audience { get; init; }

    public required ScheduleEventType EventType { get; init; }

    public required CandidateRecordStatus Status { get; init; }

    public string? NormalizedCourseIdentity { get; init; }

    public required string DisplayTitle { get; init; }

    public required DateOnly LocalDate { get; init; }

    /// <summary>
    /// The local times, or <c>null</c> on both when the item is all-day
    /// (ADR-046). A parser states both or neither.
    /// </summary>
    public TimeOnly? StartLocalTime { get; init; }

    public TimeOnly? EndLocalTime { get; init; }

    /// <summary>
    /// Whether the item occupies the whole local date. A dated holiday or
    /// semester-break row is all-day; no time is invented for it.
    /// </summary>
    public bool IsAllDay { get; init; }

    public required string TimeZoneId { get; init; }

    public string? Instructor { get; init; }

    public string? Location { get; init; }

    /// <summary>
    /// The curriculum block the lesson belongs to, when the source states it
    /// (ADR-047). Never derived from the lesson title.
    /// </summary>
    public string? CurriculumBlock { get; init; }

    /// <summary>
    /// Every academic department the source explicitly names, in source order
    /// (ADR-049). An integrated session names several; empty means the source
    /// named none.
    /// </summary>
    public IReadOnlyList<string> Departments { get; init; } = [];

    public required string StableIdentity { get; init; }

    public required string ContentHash { get; init; }

    public decimal Confidence { get; init; }

    public IReadOnlyList<IdentityComponent> IdentityComponents { get; init; } = [];

    public IReadOnlyList<SourceEvidence> Evidence { get; init; } = [];
}

public enum ProgramLanguage
{
    Turkish,
    English,
}

public enum ScheduleEventType
{
    Theory,
    Practice,
    FreeStudy,
    AnatomyPractice,
    BedsidePractice,
    FacultyPractice,
    VerticalCorridor,
    IntegratedSession,
    Exam,
    Other,
}

public enum CandidateRecordStatus
{
    Scheduled,
    Cancelled,
}

public sealed record ScheduleAudienceCandidate
{
    public required AudienceScope Scope { get; init; }

    public IReadOnlyList<AudienceSelector> Selectors { get; init; } = [];
}

public enum AudienceScope
{
    AllStudentsInProgram,
    SelectedGroups,
}

public sealed record AudienceSelector
{
    public required string Dimension { get; init; }

    public required string Value { get; init; }
}

public sealed record IdentityComponent
{
    public required string Name { get; init; }

    public required string Value { get; init; }
}

public sealed record SourceEvidence
{
    public required string SheetId { get; init; }

    public required string SheetTitle { get; init; }

    public required string Range { get; init; }

    public string? RawText { get; init; }

    public required string ExtractionRule { get; init; }
}

public sealed record ParserWarning
{
    public required ParserWarningSeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? CandidateId { get; init; }

    public SourceEvidence? Evidence { get; init; }
}

public enum ParserWarningSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record ParserMetric
{
    public required string Name { get; init; }

    public required decimal Value { get; init; }

    public string? Unit { get; init; }
}

public sealed record ConfidenceIndicator
{
    public required string Field { get; init; }

    public required decimal Score { get; init; }

    public required string Reason { get; init; }

    public string? CandidateId { get; init; }
}
