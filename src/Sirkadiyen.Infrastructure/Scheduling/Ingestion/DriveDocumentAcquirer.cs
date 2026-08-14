using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Scheduling.Ingestion;

/// <summary>
/// Acquires a Drive-published source by downloading its file and converting it
/// onto the normalized snapshot contract (ADR-083).
/// </summary>
/// <remarks>
/// Both Office formats are converted onto the same normalized snapshot contract
/// (ADR-076), so only the reader and the expected MIME type differ. The Grade 2
/// vertical-corridor calendars are Word documents; the Grade 3 annual, faculty
/// practice and practice-location sources are workbooks published on the same
/// transport.
/// </remarks>
public sealed class DriveDocumentAcquirer(
    IGoogleDriveFileClient driveClient,
    DocxSnapshotConverter docxConverter,
    LocalXlsxSnapshotConverter xlsxConverter) : IDriveDocumentAcquirer
{
    /// <summary>
    /// The largest source document one acquisition reads into memory. The same
    /// bound the upload path applies (ADR-080), for the same reason: the real
    /// documents are tens of kilobytes.
    /// </summary>
    public const long MaximumDocumentBytes = 8 * 1024 * 1024;

    private const string DocxMimeType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string XlsxMimeType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Every OOXML document is a ZIP container, and this is its local file header.
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public bool CanAcquire(ScheduleDocumentFormat format) =>
        format is ScheduleDocumentFormat.Docx or ScheduleDocumentFormat.Xlsx;

    public async Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
        ScheduleDocumentFormat format,
        AcquireSpreadsheetSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SpreadsheetId);

        if (!CanAcquire(format))
        {
            throw new NotSupportedException(
                $"A {format} document published on Google Drive cannot be converted yet.");
        }

        DriveFile file = await driveClient.FetchAsync(
            new DriveFileRequest
            {
                // A Drive source stores its file identifier as the external ID,
                // and the poller carries it here.
                FileId = request.SpreadsheetId,
                ExpectedMimeType = format is ScheduleDocumentFormat.Xlsx
                    ? XlsxMimeType
                    : DocxMimeType,
                MaximumBytes = MaximumDocumentBytes,
            },
            cancellationToken);

        RequireOfficeContainer(file);

        return format is ScheduleDocumentFormat.Xlsx
            ? xlsxConverter.ConvertDownload(file.Content, request)
            : docxConverter.ConvertDownload(file.Content, request);
    }

    /// <summary>
    /// Refuses a payload that is not an Office container before the document
    /// reader sees it.
    /// </summary>
    /// <remarks>
    /// Drive's MIME type states what the file is recorded as, not what arrived. A
    /// sign-in or error page served with a success status is the failure this
    /// catches, and naming it here keeps it from surfacing as an unreadable-package
    /// error that reads like a damaged document.
    /// </remarks>
    private static void RequireOfficeContainer(DriveFile file)
    {
        if (file.Content.Span.StartsWith(ZipSignature))
        {
            return;
        }

        throw new DriveDocumentException(
            file.FileId,
            DriveDocumentFailure.CorruptContent,
            $"The download of Google Drive file '{file.FileId}' is not an Office document "
            + "container. The response was served as the document but is not one.");
    }
}
