using Sirkadiyen.Application.Operations;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Prunes one operator-chosen snapshot's payload on demand (ADR-120), the manual counterpart to the
/// automatic ADR-044 retention batch. It reclaims the large normalized document while keeping the
/// snapshot's immutable identity and every downstream parse/revision/diff decision.
/// </summary>
/// <remarks>
/// A snapshot is evidence (AI_GUIDELINE §9), so this refuses to drop a payload that is still needed:
/// a snapshot whose scope is frozen, the newest per source (kept so a parser-profile change can
/// reparse it), the first snapshot of the source's current academic year (its baseline), and any
/// snapshot whose parse run has not reached a terminal, successful state yet. The refusal reasons are
/// computed against the same rules as the retention batch, minus that batch's recent-time window: the
/// operator's decision that a snapshot is old enough replaces the window, everything else still holds.
/// </remarks>
public sealed class SnapshotPayloadPruneService(
    ISnapshotRetentionStore store,
    IOperationalFreezeStore freezeStore,
    TimeProvider timeProvider)
{
    public async Task<SnapshotPayloadPruneResult> PruneAsync(
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        SnapshotPruneCandidate? candidate =
            await store.FindPruneCandidateAsync(snapshotId, cancellationToken);

        if (candidate is null)
        {
            return new SnapshotPayloadPruneResult
            {
                Outcome = SnapshotPayloadPruneOutcome.SnapshotNotFound,
                Detail = $"No snapshot with ID '{snapshotId}' exists.",
            };
        }

        if (candidate.PayloadAlreadyPruned)
        {
            return Result(
                SnapshotPayloadPruneOutcome.AlreadyPruned,
                candidate,
                "The snapshot's payload has already been pruned.");
        }

        // The freeze is read through the port so the global emergency stop and the source's scoped
        // control are both honoured, exactly as every other mutating pipeline boundary does.
        if (await freezeStore.IsFrozenAsync(candidate.Scope, cancellationToken))
        {
            return Result(
                SnapshotPayloadPruneOutcome.Frozen,
                candidate,
                "The pipeline for this source's class/program is frozen. Lift the freeze and retry.");
        }

        if (candidate.IneligibleReason is { } reason)
        {
            return Result(SnapshotPayloadPruneOutcome.NotEligible, candidate, reason);
        }

        bool pruned = await store.PrunePayloadAsync(
            snapshotId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        // A false result means the payload disappeared between the check and the write — the
        // retention batch or a concurrent prune won the race. That is the same no-op outcome.
        return pruned
            ? Result(SnapshotPayloadPruneOutcome.Pruned, candidate)
            : Result(
                SnapshotPayloadPruneOutcome.AlreadyPruned,
                candidate,
                "The snapshot's payload has already been pruned.");
    }

    private static SnapshotPayloadPruneResult Result(
        SnapshotPayloadPruneOutcome outcome,
        SnapshotPruneCandidate candidate,
        string? detail = null) => new()
        {
            Outcome = outcome,
            SnapshotId = candidate.SnapshotId,
            SourceId = candidate.SourceId.Value,
            AcquiredAtUtc = candidate.AcquiredAtUtc,
            Detail = detail,
        };
}

public sealed record SnapshotPayloadPruneResult
{
    public required SnapshotPayloadPruneOutcome Outcome { get; init; }

    public Guid? SnapshotId { get; init; }

    public string? SourceId { get; init; }

    public DateTimeOffset? AcquiredAtUtc { get; init; }

    /// <summary>A human-readable explanation for a non-success outcome.</summary>
    public string? Detail { get; init; }
}

public enum SnapshotPayloadPruneOutcome
{
    Pruned,
    AlreadyPruned,
    SnapshotNotFound,
    Frozen,
    NotEligible,
}
