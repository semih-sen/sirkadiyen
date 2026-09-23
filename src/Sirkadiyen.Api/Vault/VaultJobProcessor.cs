using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Api.Vault;

/// <summary>
/// Runs queued vault note jobs one at a time. One reader is the concurrency limit: the agent runs on
/// a personal subscription, and parallel runs would only reach its usage limit sooner.
/// </summary>
internal sealed class VaultJobProcessor(
    VaultJobRegistry jobs,
    VaultNoteJobService service,
    ILogger<VaultJobProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        service.SweepWorkspaceRoot();

        try
        {
            await foreach (Guid jobId in jobs.ReadQueueAsync(stoppingToken))
            {
                logger.LogInformation("Vault note job {JobId} started.", jobId);
                await service.RunAsync(jobId, stoppingToken);

                VaultJobView? job = jobs.Find(jobId);
                if (job?.Status == VaultJobStatus.Succeeded)
                {
                    logger.LogInformation(
                        "Vault note job {JobId} wrote {NotePath}; {UpdatedCount} backlinks added, {WarningCount} warnings.",
                        jobId,
                        job.NotePath,
                        job.Backlinks.Count(static outcome => outcome.Status == VaultBacklinkStatus.Updated),
                        job.Warnings.Count);
                }
                else
                {
                    logger.LogWarning("Vault note job {JobId} failed: {Error}", jobId, job?.Error);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown; queued jobs are lost with the process (ADR-168).
        }
    }
}
