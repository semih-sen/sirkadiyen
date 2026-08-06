namespace Sirkadiyen.Application.Scheduling.Ingestion;

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
