using Sirkadiyen.Domain.ScheduleDiffing;

namespace Sirkadiyen.Application.ScheduleDiffing;

/// <summary>
/// The operator path for a held diff (ADR-042).
/// </summary>
/// <remarks>
/// A diff held by the dispatch gate (ADR-040) stops there. Without this, the
/// only way forward is correcting the source and waiting for the next revision,
/// which is right when the hold reveals a parse fault and wrong when the source
/// really did drop a hundred lessons at the end of a semester.
/// </remarks>
public sealed class ScheduleDiffReviewService(
    IScheduleDiffReviewStore store,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<ScheduleDiffSummary>> ListAsync(
        ScheduleDiffState state,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return store.ListByStateAsync(state, limit, cancellationToken);
    }

    public Task<ScheduleDiffDetail?> FindAsync(
        Guid scheduleDiffId,
        int entryLimit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryLimit);

        return store.FindAsync(scheduleDiffId, entryLimit, cancellationToken);
    }

    /// <summary>Releases a held diff on a named operator's behalf.</summary>
    public Task<ScheduleDiffReleaseResult> ReleaseAsync(
        Guid scheduleDiffId,
        string releasedBy,
        string releaseReason,
        CancellationToken cancellationToken) =>
        store.ReleaseAsync(
            scheduleDiffId,
            releasedBy,
            releaseReason,
            timeProvider.GetUtcNow(),
            cancellationToken);
}
