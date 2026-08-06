using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.Scheduling.Ingestion;

/// <summary>
/// A source an administrator may upload a document for.
/// </summary>
/// <remarks>
/// It carries the shared-document group so the caller can say which other sources
/// one upload will serve (ADR-080), and the expected document format so it can
/// refuse a file the endpoint would refuse anyway. It deliberately carries no poll
/// timestamps: an upload source is never polled, so those are always absent and
/// the upload history is the audit endpoint's answer.
/// </remarks>
public sealed record UploadableSourceView
{
    public required string SourceId { get; init; }

    public required string DisplayName { get; init; }

    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required ScheduleDocumentFormat DocumentFormat { get; init; }

    /// <summary>
    /// The group of sources served by literally the same file, or
    /// <see langword="null"/> when this source has its own document.
    /// </summary>
    public string? SharedDocumentGroup { get; init; }

    /// <summary>
    /// The administratively acquired sources of a catalog, ordered by identifier so
    /// the list a UI renders does not depend on catalog order.
    /// </summary>
    public static IReadOnlyList<UploadableSourceView> SelectUploadable(
        IReadOnlyList<ScheduleSource> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return
        [
            .. catalog
                .Where(source => source.Transport is ScheduleSourceTransport.AdministrativeUpload)
                .OrderBy(source => source.SourceId.Value, StringComparer.Ordinal)
                .Select(From),
        ];
    }

    public static UploadableSourceView From(ScheduleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new UploadableSourceView
        {
            SourceId = source.SourceId.Value,
            DisplayName = source.DisplayName,
            AcademicYear = source.AcademicYear,
            ClassYear = source.ClassYear,
            ProgramLanguage = source.ProgramLanguage,
            DocumentFormat = source.DocumentFormat,
            SharedDocumentGroup = source.SharedDocumentGroup,
        };
    }
}

public sealed record UploadResponse
{
    /// <summary>The digest of the uploaded bytes, which identifies the file itself.</summary>
    public required string ContentSha256 { get; init; }

    /// <summary>Every source the document became evidence for, and what changed.</summary>
    public required IReadOnlyList<UploadTargetResponse> Targets { get; init; }

    public static UploadResponse From(DocumentUploadResult result) => new()
    {
        ContentSha256 = result.ContentSha256!,
        Targets = [.. result.Targets.Select(target => new UploadTargetResponse
        {
            SourceId = target.SourceId,
            ClassYear = target.ClassYear,
            ProgramLanguage = target.ProgramLanguage,
            Outcome = target.Outcome,
            SnapshotId = target.SnapshotId,
        })],
    };
}

public sealed record UploadTargetResponse
{
    public required string SourceId { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required SourceDocumentUploadOutcome Outcome { get; init; }

    public Guid? SnapshotId { get; init; }
}

public sealed record UploadAuditEntry
{
    public required string SourceId { get; init; }

    public required string UploadedBy { get; init; }

    public required string FileName { get; init; }

    public required long ByteCount { get; init; }

    public required string ContentSha256 { get; init; }

    public required SourceDocumentUploadOutcome Outcome { get; init; }

    public required DateTimeOffset UploadedAtUtc { get; init; }

    public static UploadAuditEntry From(SourceDocumentUpload upload) => new()
    {
        SourceId = upload.SourceId.Value,
        UploadedBy = upload.UploadedBy,
        FileName = upload.FileName,
        ByteCount = upload.ByteCount,
        ContentSha256 = upload.ContentSha256,
        Outcome = upload.Outcome,
        UploadedAtUtc = upload.UploadedAtUtc,
    };
}
