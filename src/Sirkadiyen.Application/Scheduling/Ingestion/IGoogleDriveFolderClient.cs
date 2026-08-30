namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Lists the documents a Drive folder holds, for sources the faculty republishes
/// rather than edits in place (ADR-133).
/// </summary>
/// <remarks>
/// This is a separate port from <see cref="IGoogleDriveFileClient"/> because it
/// answers a different question and needs a different permission: reading one
/// file by ID does not require being able to enumerate the folder it sits in, and
/// a deployment may be granted one and not the other.
/// </remarks>
public interface IGoogleDriveFolderClient
{
    /// <summary>
    /// The documents the folder currently holds, excluding trashed files and
    /// sub-folders.
    /// </summary>
    Task<IReadOnlyList<DriveFolderEntry>> ListAsync(
        DriveFolderListRequest request,
        CancellationToken cancellationToken);
}

public sealed record DriveFolderListRequest
{
    /// <summary>The Drive folder identifier the source declares.</summary>
    public required string FolderId { get; init; }

    /// <summary>
    /// The MIME type a listed document must have to be a candidate.
    /// </summary>
    /// <remarks>
    /// Folders in practice collect more than the published document — an older
    /// export, a PDF someone dropped in. Filtering in the query rather than after
    /// it keeps the choice from ever depending on a file the source could not be.
    /// </remarks>
    public required string ExpectedMimeType { get; init; }

    /// <summary>The most entries one listing may read.</summary>
    public required int MaximumEntries { get; init; }
}

/// <summary>One document in a Drive folder, as Drive describes it.</summary>
public sealed record DriveFolderEntry
{
    public required string FileId { get; init; }

    /// <summary>The Drive file name, kept as evidence and never used as a path.</summary>
    public required string Name { get; init; }

    public required string MimeType { get; init; }

    /// <summary>When Drive last recorded a change to the document.</summary>
    public required DateTimeOffset ModifiedAtUtc { get; init; }
}
