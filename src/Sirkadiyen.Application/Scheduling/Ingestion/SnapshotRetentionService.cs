using Sirkadiyen.Application.Operations;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Applies the active-year anchor plus recent-window snapshot retention policy.
/// </summary>
public sealed class SnapshotRetentionService(
    ISnapshotRetentionStore store,
    IOperationalFreezeStore freezeStore,
    SnapshotRetentionOptions options,
    TimeProvider timeProvider)
{
    public async Task<SnapshotRetentionResult> RunAsync(CancellationToken cancellationToken)
    {
        options.Validate();

        OperationalFreezeSnapshot freeze = await freezeStore.GetAsync(cancellationToken);
        if (freeze.IsFrozen)
        {
            return new SnapshotRetentionResult
            {
                Outcome = SnapshotRetentionOutcome.Frozen,
                CutoffUtc = timeProvider.GetUtcNow() - options.RecentWindow,
                Pruned = [],
            };
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset cutoff = now - options.RecentWindow;
        IReadOnlyList<PrunedSnapshotPayload> pruned =
            await store.PruneExpiredPayloadsAsync(
                cutoff,
                now,
                options.BatchSize,
                cancellationToken);

        return new SnapshotRetentionResult
        {
            Outcome = SnapshotRetentionOutcome.Completed,
            CutoffUtc = cutoff,
            Pruned = pruned,
        };
    }
}

public sealed record SnapshotRetentionResult
{
    public required SnapshotRetentionOutcome Outcome { get; init; }

    public required DateTimeOffset CutoffUtc { get; init; }

    public required IReadOnlyList<PrunedSnapshotPayload> Pruned { get; init; }
}

public enum SnapshotRetentionOutcome
{
    Completed,
    Frozen,
}
