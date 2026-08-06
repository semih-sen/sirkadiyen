using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Contracts.Spreadsheets;

namespace Sirkadiyen.Infrastructure.Scheduling.Ingestion;

public sealed class GoogleSheetsSnapshotAcquirer(
    SheetsService sheetsService,
    GoogleSheetsSnapshotMapper mapper) : ISpreadsheetSnapshotAcquirer
{
    public async Task<NormalizedSpreadsheetSnapshot> AcquireAsync(
        AcquireSpreadsheetSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        SpreadsheetsResource.GetRequest apiRequest =
            sheetsService.Spreadsheets.Get(request.SpreadsheetId);
        apiRequest.IncludeGridData = true;
        apiRequest.Ranges = request.Ranges
            .Where(static range => !string.IsNullOrWhiteSpace(range))
            .Select(static range => range.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Spreadsheet spreadsheet = await apiRequest.ExecuteAsync(cancellationToken);
        return mapper.Map(spreadsheet, request);
    }

    private static void Validate(AcquireSpreadsheetSnapshotRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SnapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SpreadsheetId);

        if (request.AcquiredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The acquisition timestamp must be expressed in UTC.",
                nameof(request));
        }
    }
}
