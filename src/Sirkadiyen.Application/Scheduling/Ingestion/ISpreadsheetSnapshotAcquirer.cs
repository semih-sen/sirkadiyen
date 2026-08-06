using Sirkadiyen.Contracts.Spreadsheets;

namespace Sirkadiyen.Application.ScheduleIngestion;

public interface ISpreadsheetSnapshotAcquirer
{
    Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
        AcquireSpreadsheetSnapshotRequest request,
        CancellationToken cancellationToken);
}
