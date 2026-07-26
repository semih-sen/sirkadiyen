using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Domain.ScheduleIngestion;

/// <summary>
/// One append-only record of an administrator uploading a source document.
/// </summary>
/// <remarks>
/// An upload is the acquisition step for a source that is handed out rather than
/// published (ADR-079), so it is the moment a person, not a poll, decides what a
/// source contains. That decision can move hundreds of calendar events, so it is
/// audited the way license and freeze changes are: who, what file, how many bytes,
/// which digest, and which snapshot it became.
/// <para>
/// One row is written per target source, because a single upload can serve several
/// (ADR-080) and each of them acquires its own evidence. A row is written even when
/// the content matched what the source already held: knowing that an administrator
/// re-uploaded an unchanged file is exactly what explains why nothing happened.
/// </para>
/// </remarks>
public sealed class SourceDocumentUpload
{
    public const int MaximumActorLength = 200;

    public const int MaximumFileNameLength = 260;

    public const int MaximumCorrelationIdLength = 100;

    /// <summary>Length of a hex-encoded SHA-256 digest.</summary>
    public const int ContentHashLength = 64;

    private SourceDocumentUpload()
    {
        // Materialization constructor.
        UploadedBy = string.Empty;
        FileName = string.Empty;
        ContentSha256 = string.Empty;
        CorrelationId = string.Empty;
    }

    public SourceDocumentUpload(
        SourceId sourceId,
        Guid scheduleSourceId,
        string uploadedBy,
        string fileName,
        long byteCount,
        string contentSha256,
        SourceDocumentUploadOutcome outcome,
        Guid snapshotId,
        string correlationId,
        DateTimeOffset uploadedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);

        if (uploadedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "An upload time must be expressed in UTC.",
                nameof(uploadedAtUtc));
        }

        if (contentSha256?.Length != ContentHashLength)
        {
            throw new ArgumentException(
                $"A content digest must be {ContentHashLength} hex characters.",
                nameof(contentSha256));
        }

        Id = Guid.CreateVersion7();
        SourceId = sourceId;
        ScheduleSourceId = scheduleSourceId;
        UploadedBy = RequiredBounded(uploadedBy, MaximumActorLength, nameof(uploadedBy));
        FileName = RequiredBounded(fileName, MaximumFileNameLength, nameof(fileName));
        ByteCount = byteCount;
        ContentSha256 = contentSha256;
        Outcome = outcome;
        SnapshotId = snapshotId;
        CorrelationId = RequiredBounded(
            correlationId,
            MaximumCorrelationIdLength,
            nameof(correlationId));
        UploadedAtUtc = uploadedAtUtc;
    }

    public Guid Id { get; private init; }

    /// <summary>The source this upload became evidence for.</summary>
    public SourceId SourceId { get; private init; }

    public Guid ScheduleSourceId { get; private init; }

    /// <summary>The administrator responsible, recorded as their identity, never their token.</summary>
    public string UploadedBy { get; private init; }

    /// <summary>The file name as submitted, kept verbatim as evidence and never used as a path.</summary>
    public string FileName { get; private init; }

    public long ByteCount { get; private init; }

    /// <summary>
    /// The digest of the uploaded bytes, which is not the snapshot's content hash:
    /// this identifies the file, that identifies the normalized content.
    /// </summary>
    public string ContentSha256 { get; private init; }

    public SourceDocumentUploadOutcome Outcome { get; private init; }

    /// <summary>The snapshot the upload became, or the matching one it did not replace.</summary>
    public Guid SnapshotId { get; private init; }

    public string CorrelationId { get; private init; }

    public DateTimeOffset UploadedAtUtc { get; private init; }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }
}

public enum SourceDocumentUploadOutcome
{
    /// <summary>The upload differed from what the source held and became a new snapshot.</summary>
    Stored,

    /// <summary>The upload normalized to content the source already held.</summary>
    Unchanged,
}
