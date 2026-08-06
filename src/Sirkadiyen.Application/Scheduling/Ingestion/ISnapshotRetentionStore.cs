using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Prunes expired normalized snapshot documents while retaining their metadata.
/// </summary>
public interface ISnapshotRetentionStore
{
    Task<IReadOnlyList<PrunedSnapshotPayload>> PruneExpiredPayloadsAsync(
        DateTimeOffset cutoffUtc,
        DateTimeOffset prunedAtUtc,
        int batchSize,
        CancellationToken cancellationToken);
}

public sealed record PrunedSnapshotPayload
{
    public required Guid SnapshotId { get; init; }

    public required SourceId SourceId { get; init; }

    public required DateTimeOffset AcquiredAtUtc { get; init; }
}
