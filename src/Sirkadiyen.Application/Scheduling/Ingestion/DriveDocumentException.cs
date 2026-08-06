namespace Sirkadiyen.Application.Scheduling.Ingestion;

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
