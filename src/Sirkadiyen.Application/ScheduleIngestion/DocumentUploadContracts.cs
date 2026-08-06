using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.ScheduleIngestion;

public sealed record DocumentUploadRequest
{
    public required string SourceId { get; init; }

    /// <summary>The submitted file name, kept as evidence and never used as a path.</summary>
    public required string FileName { get; init; }

    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>The administrator responsible for this acquisition.</summary>
    public required string UploadedBy { get; init; }

    public required string CorrelationId { get; init; }
}

public sealed record DocumentUploadResult
{
    public required DocumentUploadOutcome Outcome { get; init; }

    public string? Detail { get; init; }

    /// <summary>The digest of the uploaded bytes, absent when nothing was accepted.</summary>
    public string? ContentSha256 { get; init; }

    /// <summary>What happened for each source the document serves.</summary>
    public required IReadOnlyList<DocumentUploadTargetResult> Targets { get; init; }
}

public sealed record DocumentUploadTargetResult
{
    public required string SourceId { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required SourceDocumentUploadOutcome Outcome { get; init; }

    public Guid? SnapshotId { get; init; }
}

public enum DocumentUploadOutcome
{
    Accepted,
    SourceNotFound,
    SourceIsNotUploadable,
    UnsupportedDocumentFormat,
    EmptyDocument,
    DocumentTooLarge,
    Frozen,
}
