using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.ScheduleSources;

namespace Sirkadiyen.Worker;

internal sealed class Worker(
    IServiceScopeFactory scopeFactory,
    ScheduleSourceCatalogLoader catalogLoader,
    WorkerOptions options,
    AdaptivePollingIntervalPolicy intervalPolicy,
    TimeProvider timeProvider,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Sirkadiyen worker started.");

        await SeedSourceCatalogAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAllSourcesAsync(stoppingToken);

            TimeSpan interval = intervalPolicy.GetInterval(timeProvider.GetUtcNow());
            logger.LogInformation(
                "Schedule polling cycle completed. Next cycle starts in {PollingInterval}.",
                interval);
            await Task.Delay(interval, timeProvider, stoppingToken);
        }
    }

    private async Task SeedSourceCatalogAsync(CancellationToken cancellationToken)
    {
        ScheduleSourceCatalog catalog = await catalogLoader.LoadAsync(
            options.SourceCatalogPath,
            cancellationToken);
        IReadOnlyCollection<ScheduleSource> sources =
            [.. catalog.Sources.Select(static source => source.ToScheduleSource())];

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IScheduleSourceStore store = scope.ServiceProvider
            .GetRequiredService<IScheduleSourceStore>();
        int changed = await store.UpsertAsync(sources, cancellationToken);
        logger.LogInformation(
            "Schedule source catalog loaded with {SourceCount} sources; {ChangedCount} rows changed.",
            sources.Count,
            changed);
    }

    private async Task PollAllSourcesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScheduleSource> sources;
        await using (AsyncServiceScope listScope = scopeFactory.CreateAsyncScope())
        {
            IScheduleSourceStore store = listScope.ServiceProvider
                .GetRequiredService<IScheduleSourceStore>();
            sources = await store.ListAsync(onlyPollingEnabled: true, cancellationToken);
        }

        foreach (ScheduleSource source in sources)
        {
            try
            {
                await using AsyncServiceScope sourceScope = scopeFactory.CreateAsyncScope();
                ScheduleSourcePoller poller = sourceScope.ServiceProvider
                    .GetRequiredService<ScheduleSourcePoller>();
                ScheduleSourcePollResult result = await poller.PollAsync(
                    source,
                    cancellationToken);
                logger.LogInformation(
                    "Source {SourceId} poll completed with {Outcome}; snapshot changed: {Changed}; "
                    + "parse run: {ParseRunId}; revision: {RevisionId}.",
                    result.SourceId,
                    result.Outcome,
                    result.SnapshotChanged,
                    result.ParseRunId,
                    result.RevisionId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Polling source {SourceId} failed.", source.SourceId);
            }
        }
    }
}
