using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.ScheduleIngestion;

/// <summary>
/// Stores acquired snapshots and answers whether a source actually changed.
/// </summary>
public interface ISourceSnapshotStore
{
    /// <summary>
    /// Stores a snapshot unless the source's latest snapshot already holds the
    /// same content.
    /// </summary>
    /// <remarks>
    /// This is the unchanged-source short circuit. Sources are polled far more
    /// often than they change, and storing an identical snapshot would both
    /// waste storage and start a parse, a diff and a synchronization pass that
    /// have nothing to do.
    /// </remarks>
    Task<StoreSnapshotResult> StoreIfChangedAsync(
        SourceId sourceId,
        NormalizedSpreadsheetSnapshot snapshot,
        CancellationToken cancellationToken);

    Task<SourceSnapshot?> GetLatestAsync(SourceId sourceId, CancellationToken cancellationToken);
}

public sealed record StoreSnapshotResult
{
    public required StoreSnapshotOutcome Outcome { get; init; }

    /// <summary>
    /// The stored snapshot when the content changed, or the existing snapshot
    /// that matched when it did not.
    /// </summary>
    public required SourceSnapshot Snapshot { get; init; }

    public bool Changed => Outcome is StoreSnapshotOutcome.Stored;
}

public enum StoreSnapshotOutcome
{
    Stored,
    Unchanged,
}
