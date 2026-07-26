using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.ScheduleIngestion;

/// <summary>
/// Acquires a source whose document is a file fetched from Google Drive, and
/// returns it on the same normalized snapshot contract the sheet sources produce.
/// </summary>
/// <remarks>
/// The download is transport and the conversion is format, and ADR-015 keeps
/// those apart: this port composes the two so the poller stays a pipeline and
/// does not learn which document library reads which extension.
/// <para>
/// <see cref="CanAcquire"/> exists because Drive holds more formats than the
/// pipeline can currently read. The transport is implemented; a format without a
/// converter is a different, smaller gap, and the poller reports it as such
/// rather than claiming the transport is missing.
/// </para>
/// </remarks>
public interface IDriveDocumentAcquirer
{
    /// <summary>Whether a document of this format can be converted after download.</summary>
    bool CanAcquire(ScheduleDocumentFormat format);

    /// <summary>
    /// Downloads and converts the file named by
    /// <see cref="AcquireSpreadsheetSnapshotRequest.SpreadsheetId"/>, which for a
    /// Drive source is the Drive file identifier.
    /// </summary>
    Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
        ScheduleDocumentFormat format,
        AcquireSpreadsheetSnapshotRequest request,
        CancellationToken cancellationToken);
}
