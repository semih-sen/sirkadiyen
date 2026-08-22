using Sirkadiyen.Domain.Scheduling.Diffing;

namespace Sirkadiyen.Application.Scheduling.Diffing;

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

    /// <summary>
    /// Lists diffs by their calendar dispatch progress, which is how the failed queue is found
    /// (ADR-097).
    /// </summary>
    public Task<IReadOnlyList<ScheduleDiffSummary>> ListByDispatchStateAsync(
        CalendarDispatchState dispatchState,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return store.ListByDispatchStateAsync(dispatchState, limit, cancellationToken);
    }

    /// <summary>
    /// Lists the most recent diffs in any state, newest first, for the history view (ADR-127).
    /// </summary>
    public Task<IReadOnlyList<ScheduleDiffSummary>> ListRecentAsync(
        int limit,
        string? sourceId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return store.ListRecentAsync(limit, sourceId, cancellationToken);
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

    /// <summary>
    /// Returns a terminally failed diff to the dispatch queue on a named operator's behalf
    /// (ADR-097). The worker performs the fan-out; this only makes it eligible again.
    /// </summary>
    public Task<ScheduleDiffRetryResult> RetryDispatchAsync(
        Guid scheduleDiffId,
        string retriedBy,
        string retryReason,
        CancellationToken cancellationToken) =>
        store.RetryDispatchAsync(
            scheduleDiffId,
            retriedBy,
            retryReason,
            timeProvider.GetUtcNow(),
            cancellationToken);

    /// <summary>
    /// Discards a held diff on a named operator's behalf so it is never dispatched (ADR-127). The
    /// schedule is corrected by a superseding revision, not by mutating this diff (ADR-033).
    /// </summary>
    public Task<ScheduleDiffDiscardResult> DiscardAsync(
        Guid scheduleDiffId,
        string discardedBy,
        string discardReason,
        CancellationToken cancellationToken) =>
        store.DiscardAsync(
            scheduleDiffId,
            discardedBy,
            discardReason,
            timeProvider.GetUtcNow(),
            cancellationToken);
}
