using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Api.Vault;

/// <summary>
/// Runs queued vault note jobs one at a time. One reader is the concurrency limit: the agent runs on
/// a personal subscription, and parallel runs would only reach its usage limit sooner. Each job gets
/// its own scope, so its database context lives exactly as long as the job.
/// </summary>
internal sealed class VaultJobProcessor(
    VaultJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<VaultJobProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);

            await foreach (Guid jobId in queue.ReadAllAsync(stoppingToken))
            {
                await RunAsync(jobId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown. A job still queued stays queued in the table and runs after the
            // next start (ADR-169).
        }
    }

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<VaultNoteJobService>().SweepWorkspaceRoot();

        try
        {
            VaultJobRecovery recovery = await scope.ServiceProvider
                .GetRequiredService<VaultJobRegistry>()
                .RecoverAsync(stoppingToken);
            if (recovery.Requeued > 0 || recovery.Interrupted > 0)
            {
                logger.LogWarning(
                    "Vault job recovery: {Requeued} queued jobs queued again, {Interrupted} interrupted jobs marked failed.",
                    recovery.Requeued,
                    recovery.Interrupted);
            }
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // New submissions still run; what the previous process left stays in the table until the
            // next start tries again.
            logger.LogError(exception, "Vault job recovery failed; leftover jobs were not picked up.");
        }
    }

    private async Task RunAsync(Guid jobId, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            VaultNoteJobService service = scope.ServiceProvider.GetRequiredService<VaultNoteJobService>();
            VaultJobRegistry jobs = scope.ServiceProvider.GetRequiredService<VaultJobRegistry>();

            logger.LogInformation("Vault note job {JobId} started.", jobId);
            await service.RunAsync(jobId, stoppingToken);

            VaultJobView? job = await jobs.FindAsync(jobId, CancellationToken.None);
            if (job?.Status == VaultJobStatus.Succeeded)
            {
                logger.LogInformation(
                    "Vault note job {JobId} wrote {NotePath}; {UpdatedCount} backlinks added, {WarningCount} warnings.",
                    jobId,
                    job.NotePath,
                    job.Backlinks.Count(static outcome => outcome.Status == VaultBacklinkStatus.Updated),
                    job.Warnings.Count);
            }
            else if (job?.Status == VaultJobStatus.Failed)
            {
                logger.LogWarning("Vault note job {JobId} failed: {Error}", jobId, job.Error);
            }
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // The job service reports its own failures on the row; reaching here means the row itself
            // could not be read or written. One such job must not stop the queue.
            logger.LogError(exception, "Vault note job {JobId} could not be run.", jobId);
        }
    }
}
