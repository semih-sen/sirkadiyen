using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.ScheduleIngestion;

/// <summary>
/// Reads an uploaded document in whichever formats administrative upload supports
/// (ADR-080).
/// </summary>
/// <remarks>
/// Only DOCX is supported, because only DOCX sources are handed out rather than
/// published. An unsupported format is refused rather than guessed at: converting
/// a workbook that arrived where a Word document was expected would produce a
/// snapshot the source's parser profile cannot read, and the failure would surface
/// as a parse rejection instead of as the upload mistake it is.
/// </remarks>
public sealed class UploadedDocumentConverter(DocxSnapshotConverter docxConverter)
    : IUploadedDocumentConverter
{
    private const string DocxExtension = ".docx";

    public bool CanConvert(ScheduleDocumentFormat format, string fileName) =>
        format is ScheduleDocumentFormat.Docx
        && fileName is not null
        && fileName.EndsWith(DocxExtension, StringComparison.OrdinalIgnoreCase);

    public NormalizedSpreadsheetSnapshot Convert(
        ScheduleDocumentFormat format,
        ReadOnlyMemory<byte> content,
        AcquireSpreadsheetSnapshotRequest request) => format switch
        {
            ScheduleDocumentFormat.Docx => docxConverter.ConvertUpload(content, request),
            _ => throw new NotSupportedException(
                $"An uploaded {format} document cannot be converted."),
        };
}
