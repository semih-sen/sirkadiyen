using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.ScheduleIngestion;

/// <summary>Converts an uploaded document into the normalized snapshot contract.</summary>
/// <remarks>
/// The readers live in the infrastructure layer with their document libraries;
/// this port keeps the upload rules independent of which format arrived.
/// </remarks>
public interface IUploadedDocumentConverter
{
    /// <summary>Whether this converter reads the declared format under the submitted name.</summary>
    bool CanConvert(ScheduleDocumentFormat format, string fileName);

    NormalizedSpreadsheetSnapshot Convert(
        ScheduleDocumentFormat format,
        ReadOnlyMemory<byte> content,
        AcquireSpreadsheetSnapshotRequest request);
}
