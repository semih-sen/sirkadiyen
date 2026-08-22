using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Observability;

namespace Sirkadiyen.Worker.Health;

/// <summary>
/// Publishes this worker instance's in-process health snapshot to the shared database each cycle
/// (ADR-124), so the administration panel can see every live instance and what it is doing. Best
/// effort: a failed write is logged and never interrupts the worker.
/// </summary>
internal sealed class WorkerHeartbeatTask(
    IServiceScopeFactory scopeFactory,
    WorkerHealthState healthState,
    TimeProvider timeProvider,
    ILogger<WorkerHeartbeatTask> logger)
{
    /// <summary>Rows for instances not seen within this window are pruned on every write.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            WorkerHealthSnapshot snapshot = healthState.GetSnapshot();
            DateTimeOffset now = timeProvider.GetUtcNow();

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IWorkerHeartbeatStore store = scope.ServiceProvider
                .GetRequiredService<IWorkerHeartbeatStore>();

            await store.RecordAsync(
                new WorkerHeartbeatBeat
                {
                    InstanceId = snapshot.InstanceId,
                    StartedAtUtc = snapshot.StartedAtUtc,
                    Status = snapshot.Status,
                    CurrentStage = snapshot.CurrentStage,
                    LastActivityAtUtc = snapshot.LastActivityAtUtc,
                    NextSourcePollAtUtc = snapshot.NextSourcePollAtUtc,
                },
                now,
                now - Retention,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Heartbeat is observability, not correctness: a database hiccup here must never stop the
            // worker's actual work. The next cycle writes again.
            logger.LogWarning(exception, "Publishing the worker heartbeat failed; continuing.");
        }
    }
}
