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
