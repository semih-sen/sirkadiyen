using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Scheduling.Sources;

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

        // Ordered so a companion is acquired before the sources that read it, or a
        // source would be parsed against last cycle's companion and the change
        // would not reach a calendar until the cycle after (ADR-133).
        return SourcePollOrder.Arrange(
            await store.ListAsync(onlyPollingEnabled: true, cancellationToken));
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

            // A discovery fallback is a poll that succeeded while quietly ceasing to
            // track the source, so it is the one success worth a warning (ADR-133).
            if (result.DiscoveryOutcome is WeeklyDocumentDiscoveryOutcome.FellBackToCatalog)
            {
                logger.LogWarning(
                    "Source {SourceId} could not resolve its discovery folder ({Failure}) and "
                    + "acquired the catalogued document instead. It will keep acquiring that "
                    + "document, and newly published ones will not be seen, until the folder "
                    + "can be listed again.",
                    result.SourceId,
                    result.DiscoveryFailure?.ToString() ?? "the folder held no matching document");
            }
            else if (result.DiscoveryOutcome is not null)
            {
                logger.LogInformation(
                    "Source {SourceId} resolved its discovery folder to {DocumentName} "
                    + "({DiscoveryOutcome}).",
                    result.SourceId,
                    result.DiscoveredDocumentName,
                    result.DiscoveryOutcome);
            }

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
