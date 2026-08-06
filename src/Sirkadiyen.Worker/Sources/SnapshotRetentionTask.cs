using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Scheduling.Ingestion;

namespace Sirkadiyen.Worker.Sources;

internal sealed class SnapshotRetentionTask(
    IServiceScopeFactory scopeFactory,
    ILogger<SnapshotRetentionTask> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            SnapshotRetentionService retention = scope.ServiceProvider
                .GetRequiredService<SnapshotRetentionService>();
            SnapshotRetentionResult result = await retention.RunAsync(cancellationToken);

            if (result.Outcome is SnapshotRetentionOutcome.Frozen)
            {
                logger.LogInformation(
                    "Snapshot retention skipped because the global operational freeze is active.");
                return;
            }

            if (result.Pruned.Count > 0)
            {
                logger.LogInformation(
                    "Snapshot retention pruned {PrunedCount} payloads older than {CutoffUtc}; "
                    + "snapshot metadata and downstream evidence remain.",
                    result.Pruned.Count,
                    result.CutoffUtc);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Snapshot retention failed.");
        }
    }
}
