namespace Sirkadiyen.Application.Scheduling.Ingestion;

public sealed record AcquireSpreadsheetSnapshotRequest
{
    public required string SourceId { get; init; }

    public required string SnapshotId { get; init; }

    public required string SpreadsheetId { get; init; }

    public required DateTimeOffset AcquiredAtUtc { get; init; }

    public IReadOnlyList<string> Ranges { get; init; } = [];
}
