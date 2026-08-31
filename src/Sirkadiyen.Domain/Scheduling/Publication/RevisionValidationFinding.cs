namespace Sirkadiyen.Domain.Scheduling.Publication;

/// <summary>
/// One thing revision validation observed about a candidate revision, and how
/// seriously it was taken.
/// </summary>
/// <remarks>
/// Findings are the evidence behind a revision's state. An administrator asked
/// to approve a quarantined revision needs to know which rule stopped it and
/// which records were involved, so a finding names its rule, carries a
/// human-readable message, and points at the records it concerns. Findings are
/// never deleted when a revision is re-validated; a new validation pass writes a
/// new set, so the history of why a revision was held stays readable.
/// </remarks>
public sealed class RevisionValidationFinding
{
    private RevisionValidationFinding()
    {
        // Materialization constructor.
        Message = string.Empty;
        Detail = string.Empty;
    }

    public RevisionValidationFinding(
        Guid scheduleRevisionId,
        RevisionValidationRule rule,
        ValidationSeverity severity,
        string message,
        DateTimeOffset createdAtUtc,
        string? detail = null,
        int affectedRecordCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfNegative(affectedRecordCount);

        Id = Guid.CreateVersion7();
        ScheduleRevisionId = scheduleRevisionId;
        Rule = rule;
        Severity = severity;
        Message = message;
        Detail = detail ?? string.Empty;
        AffectedRecordCount = affectedRecordCount;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ScheduleRevisionId { get; private set; }

    public RevisionValidationRule Rule { get; private set; }

    public ValidationSeverity Severity { get; private set; }

    public string Message { get; private set; }

    /// <summary>Machine-readable JSON evidence for the finding, or empty.</summary>
    public string Detail { get; private set; }

    public int AffectedRecordCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}

/// <summary>
/// The rules revision validation applies, named so that an operator reading a
/// finding knows which guarantee was tested.
/// </summary>
public enum RevisionValidationRule
{
    /// <summary>A revision carrying no records at all.</summary>
    EmptyRevision,

    /// <summary>A record dated outside the source's academic year.</summary>
    RecordDateOutsideAcademicYear,

    /// <summary>
    /// A date contradicting the order of the column it sits in — repaired as a
    /// mistyped year, or reported with the readings that fit (ADR-139).
    /// </summary>
    RecordDateOutOfSequence,

    /// <summary>A lesson too short or too long to be real.</summary>
    ImpossibleLessonDuration,

    /// <summary>A required field the parser resolved with low confidence.</summary>
    LowConfidenceRecord,

    /// <summary>A selector value the source has not declared as supported.</summary>
    UnknownAudienceSelector,

    /// <summary>Two records in one revision claiming the same stable identity.</summary>
    DuplicateStableIdentity,

    /// <summary>The same audience booked twice at the same local date and time.</summary>
    AudienceOverlap,

    /// <summary>Records disappearing relative to the last published revision.</summary>
    MassDeletion,
}

/// <summary>
/// How a finding affects publication.
/// </summary>
/// <remarks>
/// <see cref="Error"/> quarantines the revision for review; it does not reject
/// it. Rejection is terminal and unrecoverable, so it is reserved for a revision
/// that cannot be published at all no matter who looks at it. Anything a human
/// could reasonably approve must remain approvable.
/// </remarks>
public enum ValidationSeverity
{
    Information,
    Warning,
    Error,
}
