using Sirkadiyen.Application.Operations;
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

    /// <summary>
    /// Describes one snapshot for an operator-triggered manual payload prune (ADR-120): the source
    /// scope its freeze is resolved against, whether its payload is already gone, and — when the
    /// payload is still stored — the reason it is not eligible to be pruned, or <see langword="null"/>
    /// when it is. Eligibility uses the same safety and baseline rules as the automatic retention
    /// batch, minus that batch's recent-time window (the operator's judgement replaces it).
    /// </summary>
    Task<SnapshotPruneCandidate?> FindPruneCandidateAsync(
        Guid snapshotId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes one snapshot's payload, keeping all metadata. Idempotent: returns
    /// <see langword="false"/> when the snapshot does not exist or its payload was already pruned.
    /// </summary>
    Task<bool> PrunePayloadAsync(
        Guid snapshotId,
        DateTimeOffset prunedAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>The prune-eligibility view of one snapshot (ADR-120).</summary>
public sealed record SnapshotPruneCandidate
{
    public required Guid SnapshotId { get; init; }

    public required SourceId SourceId { get; init; }

    public required DateTimeOffset AcquiredAtUtc { get; init; }

    /// <summary>The source's class/program, used to resolve the operational freeze scope.</summary>
    public required OperationalFreezeScope Scope { get; init; }

    public required bool PayloadAlreadyPruned { get; init; }

    /// <summary>
    /// Why this snapshot's payload may not be pruned, or <see langword="null"/> when it is eligible.
    /// Only meaningful when <see cref="PayloadAlreadyPruned"/> is <see langword="false"/>.
    /// </summary>
    public string? IneligibleReason { get; init; }
}

public sealed record PrunedSnapshotPayload
{
    public required Guid SnapshotId { get; init; }

    public required SourceId SourceId { get; init; }

    public required DateTimeOffset AcquiredAtUtc { get; init; }
}
