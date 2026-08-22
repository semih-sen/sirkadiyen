using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Queues operator-requested source polls and lets the worker claim them (ADR-127).
/// </summary>
/// <remarks>
/// The admin API writes a request; the worker drains and executes it. Claiming is a single
/// transactional read-and-mark so two worker instances never poll the same request twice.
/// </remarks>
public interface ISourcePollRequestStore
{
    /// <summary>Enqueues a request to poll one source now.</summary>
    Task EnqueueAsync(
        SourceId sourceId,
        bool force,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims up to <paramref name="limit"/> unclaimed requests, oldest first, marking
    /// them so no other instance takes them, and returns them for the caller to execute.
    /// </summary>
    Task<IReadOnlyList<SourcePollRequest>> ClaimPendingAsync(
        int limit,
        DateTimeOffset claimedAtUtc,
        CancellationToken cancellationToken);
}
