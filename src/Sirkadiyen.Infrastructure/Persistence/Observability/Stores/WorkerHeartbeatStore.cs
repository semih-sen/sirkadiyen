using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Observability;
using Sirkadiyen.Domain.Observability;

namespace Sirkadiyen.Infrastructure.Persistence.Observability.Stores;

/// <summary>
/// PostgreSQL store for worker-instance heartbeats (ADR-124): an upsert per instance plus a bounded
/// retention delete, and a read of every retained instance for the admin monitor.
/// </summary>
public sealed class WorkerHeartbeatStore(SirkadiyenDbContext dbContext) : IWorkerHeartbeatStore
{
    public async Task RecordAsync(
        WorkerHeartbeatBeat beat,
        DateTimeOffset heartbeatAtUtc,
        DateTimeOffset retentionCutoffUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beat);

        WorkerInstanceHeartbeat? existing = await dbContext.WorkerInstanceHeartbeats
            .SingleOrDefaultAsync(row => row.InstanceId == beat.InstanceId, cancellationToken);

        if (existing is null)
        {
            dbContext.WorkerInstanceHeartbeats.Add(WorkerInstanceHeartbeat.Create(
                beat.InstanceId,
                beat.StartedAtUtc,
                beat.Status,
                beat.CurrentStage,
                beat.LastActivityAtUtc,
                heartbeatAtUtc,
                beat.NextSourcePollAtUtc));
        }
        else
        {
            existing.Beat(
                beat.Status,
                beat.CurrentStage,
                beat.LastActivityAtUtc,
                heartbeatAtUtc,
                beat.NextSourcePollAtUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Prune instances that stopped reporting. Kept separate from the upsert's SaveChanges so a
        // delete conflict can never lose this instance's own fresh heartbeat.
        await dbContext.WorkerInstanceHeartbeats
            .Where(row => row.LastHeartbeatAtUtc < retentionCutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkerInstanceView>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.WorkerInstanceHeartbeats
            .AsNoTracking()
            .OrderByDescending(row => row.LastHeartbeatAtUtc)
            .Select(row => new WorkerInstanceView
            {
                InstanceId = row.InstanceId,
                StartedAtUtc = row.StartedAtUtc,
                Status = row.Status,
                CurrentStage = row.CurrentStage,
                LastActivityAtUtc = row.LastActivityAtUtc,
                LastHeartbeatAtUtc = row.LastHeartbeatAtUtc,
                NextSourcePollAtUtc = row.NextSourcePollAtUtc,
            })
            .ToListAsync(cancellationToken);
}
