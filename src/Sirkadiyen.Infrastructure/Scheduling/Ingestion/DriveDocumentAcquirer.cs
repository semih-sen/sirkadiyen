using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.ScheduleIngestion;

/// <summary>
/// Acquires a Drive-published source by downloading its file and converting it
/// onto the normalized snapshot contract (ADR-083).
/// </summary>
/// <remarks>
/// Only DOCX is converted, because the Drive sources that have a parser profile
/// are the two vertical-corridor calendars and both are Word documents. The
/// Grade 3 workbooks are catalogued on the same transport and will be acquirable
/// once they have profiles to read them; until then the poller reports the format
/// as unsupported rather than downloading a file nothing can interpret.
/// </remarks>
public sealed class DriveDocumentAcquirer(
    IGoogleDriveFileClient driveClient,
    DocxSnapshotConverter docxConverter) : IDriveDocumentAcquirer
{
    /// <summary>
    /// The largest source document one acquisition reads into memory. The same
    /// bound the upload path applies (ADR-080), for the same reason: the real
    /// documents are tens of kilobytes.
    /// </summary>
    public const long MaximumDocumentBytes = 8 * 1024 * 1024;

    private const string DocxMimeType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // Every OOXML document is a ZIP container, and this is its local file header.
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public bool CanAcquire(ScheduleDocumentFormat format) =>
        format is ScheduleDocumentFormat.Docx;

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
                ExpectedMimeType = DocxMimeType,
                MaximumBytes = MaximumDocumentBytes,
            },
            cancellationToken);

        RequireOfficeContainer(file);

        return docxConverter.ConvertDownload(file.Content, request);
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
