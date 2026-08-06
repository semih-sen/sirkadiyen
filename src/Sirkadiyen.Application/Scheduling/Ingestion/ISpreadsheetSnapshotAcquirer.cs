using Sirkadiyen.Contracts.Spreadsheets;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

public interface ISpreadsheetSnapshotAcquirer
{
    Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
        AcquireSpreadsheetSnapshotRequest request,
        CancellationToken cancellationToken);
}
