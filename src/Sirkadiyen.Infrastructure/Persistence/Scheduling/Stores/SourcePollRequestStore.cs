using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// PostgreSQL queue of operator-requested source polls (ADR-127).
/// </summary>
/// <remarks>
/// Claiming uses <c>FOR UPDATE SKIP LOCKED</c> so two worker instances never take the same request:
/// each locks and marks a disjoint set in one statement. The mark is the claim, so a request is
/// polled exactly once even without the shared calendar fence, which does not cover polling.
/// </remarks>
public sealed class SourcePollRequestStore(SirkadiyenDbContext dbContext) : ISourcePollRequestStore
{
    public async Task EnqueueAsync(
        SourceId sourceId,
        bool force,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        dbContext.SourcePollRequests.Add(
            SourcePollRequest.Create(sourceId, force, requestedBy, requestedAtUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SourcePollRequest>> ClaimPendingAsync(
        int limit,
        DateTimeOffset claimedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // One statement claims a disjoint batch: the inner SELECT locks up to `limit` unclaimed rows
        // and skips any another instance already holds, the UPDATE marks exactly those, and the CTE
        // returns them so the result is both the claim and the work list.
        FormattableString sql = $@"
            WITH claimed AS (
                UPDATE sirkadiyen.source_poll_requests
                SET ""ClaimedAtUtc"" = {claimedAtUtc}
                WHERE ""Id"" IN (
                    SELECT ""Id""
                    FROM sirkadiyen.source_poll_requests
                    WHERE ""ClaimedAtUtc"" IS NULL
                    ORDER BY ""RequestedAtUtc""
                    LIMIT {limit}
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *
            )
            SELECT * FROM claimed
            ORDER BY ""RequestedAtUtc""";

        return await dbContext.SourcePollRequests
            .FromSql(sql)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
