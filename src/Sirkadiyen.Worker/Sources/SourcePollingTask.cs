using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.ScheduleParsing;
using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.ScheduleSources;

namespace Sirkadiyen.Worker.Sources;

internal sealed class SourcePollingTask(
    IServiceScopeFactory scopeFactory,
    ILogger<SourcePollingTask> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScheduleSource> sources = await ListSourcesAsync(cancellationToken);
        foreach (ScheduleSource source in sources)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            if (!await CanPollAsync(scope.ServiceProvider, cancellationToken))
            {
                return;
            }

            await PollAsync(scope.ServiceProvider, source, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ScheduleSource>> ListSourcesAsync(
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IScheduleSourceStore store = scope.ServiceProvider.GetRequiredService<IScheduleSourceStore>();
        return await store.ListAsync(onlyPollingEnabled: true, cancellationToken);
    }

    private async Task<bool> CanPollAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        try
        {
            IOperationalFreezeStore freeze = services.GetRequiredService<IOperationalFreezeStore>();
            OperationalFreezeSnapshot state = await freeze.GetAsync(cancellationToken);
            if (!state.IsFrozen)
            {
                return true;
            }

            logger.LogWarning(
                "Source polling is frozen. No acquisition will start. "
                + "Last change at {FreezeChangedAtUtc} by {FreezeChangedBy}: {FreezeReason}",
                state.ChangedAtUtc,
                state.ChangedBy,
                state.Reason);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The global operational freeze state could not be read. "
                + "Source polling is stopped for this cycle.");
            return false;
        }
    }

    private async Task PollAsync(
        IServiceProvider services,
        ScheduleSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            ScheduleSourcePoller poller = services.GetRequiredService<ScheduleSourcePoller>();
            ScheduleSourcePollResult result = await poller.PollAsync(source, cancellationToken);
            logger.LogInformation(
                "Source {SourceId} poll completed with {Outcome}; snapshot changed: {Changed}; "
                + "parse run: {ParseRunId}; revision: {RevisionId}.",
                result.SourceId,
                result.Outcome,
                result.SnapshotChanged,
                result.ParseRunId,
                result.RevisionId);

            if (result.ParseRunStartKind is ParseRunStartKind.RecoveredStale)
            {
                logger.LogWarning(
                    "Parse run {ParseRunId} for source {SourceId} was recovered after being "
                    + "left running by a worker that stopped mid-parse.",
                    result.ParseRunId,
                    result.SourceId);
            }
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
