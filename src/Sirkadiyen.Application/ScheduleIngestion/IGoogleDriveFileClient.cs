namespace Sirkadiyen.Application.ScheduleIngestion;

/// <summary>
/// Reads one file out of Google Drive, verified against what Drive says that file
/// is.
/// </summary>
/// <remarks>
/// <para>
/// The vertical-corridor calendars are Word documents that Student Affairs edits
/// in Drive during the year, so they must be re-acquired rather than converted
/// once (ADR-076). This is the port that reads them. The adapter lives beside the
/// other Google clients in the infrastructure layer.
/// </para>
/// <para>
/// Fetching is one operation rather than a metadata call and a download the
/// caller composes, because the checks that make a download trustworthy — that
/// the file is not in the trash, that it is the binary format the catalog
/// declared, that the bytes are the length and digest Drive stated — need both
/// halves. A caller that skipped them would still receive a plausible-looking
/// document, and a truncated schedule reads as a schedule with fewer lessons.
/// </para>
/// </remarks>
public interface IGoogleDriveFileClient
{
    Task<DriveFile> FetchAsync(DriveFileRequest request, CancellationToken cancellationToken);
}

public sealed record DriveFileRequest
{
    /// <summary>The Drive file identifier, which the catalog stores as the external ID.</summary>
    public required string FileId { get; init; }

    /// <summary>
    /// The MIME type the source's declared document format implies.
    /// </summary>
    /// <remarks>
    /// Checked before anything is downloaded. A document someone converted into a
    /// Google Doc cannot be downloaded at all, and one that is a different binary
    /// format would fail later as a conversion error rather than as the source
    /// change it is.
    /// </remarks>
    public required string ExpectedMimeType { get; init; }

    /// <summary>The largest response this fetch may read into memory.</summary>
    public required long MaximumBytes { get; init; }
}

/// <summary>One Drive file, downloaded together with what Drive states about it.</summary>
public sealed record DriveFile
{
    public required string FileId { get; init; }

    /// <summary>The Drive file name, kept as evidence and never used as a path.</summary>
    public required string Name { get; init; }

    public required string MimeType { get; init; }

    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Drive's own digest of the bytes, when it states one.</summary>
    public string? Md5Checksum { get; init; }

    /// <summary>When Drive last recorded a change, for logging rather than for change detection.</summary>
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}

/// <summary>
/// A Drive file that cannot be read as the source document it is supposed to be.
/// </summary>
/// <remarks>
/// Separate from the transient HTTP failures, which the next poll simply retries.
/// Every reason here needs a person: a document was moved, unshared, trashed,
/// converted to another format, or arrived damaged.
/// </remarks>
public sealed class DriveDocumentException(
    string fileId,
    DriveDocumentFailure failure,
    string message) : Exception(message)
{
    public string FileId { get; } = fileId;

    public DriveDocumentFailure Failure { get; } = failure;
}

public enum DriveDocumentFailure
{
    /// <summary>Drive has no such file, or the credential may not see that it exists.</summary>
    NotFound,

    /// <summary>The credential is not authorized for this file or lacks the Drive scope.</summary>
    AccessDenied,

    /// <summary>The file is in the owner's trash and is no longer a published source.</summary>
    Trashed,

    /// <summary>The file is not the binary format the source declared.</summary>
    UnexpectedFormat,

    /// <summary>The response exceeded the bound one acquisition may read into memory.</summary>
    TooLarge,

    /// <summary>The bytes do not match the length or digest Drive stated for them.</summary>
    CorruptContent,
}
