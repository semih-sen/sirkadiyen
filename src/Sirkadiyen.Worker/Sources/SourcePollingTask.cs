using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
using Sirkadiyen.Worker.Notifications;

namespace Sirkadiyen.Worker.Sources;

internal sealed class SourcePollingTask(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOperatorAlertNotifier alerts,
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

            // The event the operator asked to hear about (ADR-144). A revision only exists when
            // the document actually said something new (ADR-141), so this is a real change rather
            // than a heartbeat, and its validation state is what says whether anyone must act.
            if (result.RevisionId is { } revisionId)
            {
                await alerts.SendAsync(
                    WorkerAlerts.RevisionCreated(
                        result.SourceId,
                        revisionId,
                        result.RevisionState,
                        result.ValidationFindingCount),
                    cancellationToken);
            }

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
                await alerts.SendAsync(
                    WorkerAlerts.SourceDiscoveryFallback(
                        result.SourceId,
                        result.DiscoveryFailure?.ToString()
                            ?? "klasörde eşleşen belge bulunamadı"),
                    cancellationToken);
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
                await alerts.SendAsync(
                    WorkerAlerts.ParseRunRecovered(result.SourceId, result.ParseRunId),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Polling source {SourceId} failed.", source.SourceId);

            // And recorded where an operator can see it (ADR-137). A failed acquisition produces
            // no snapshot, no parse run and no revision, so without this the only trace is this
            // log line and a poll timestamp that stops advancing — which is how three trashed
            // Grade 3 workbooks went unnoticed for four days.
            await RecordFailureAsync(services, source, exception, cancellationToken);

            // And said out loud, because a source nobody can read produces nothing at all: no
            // snapshot, no revision, and therefore not one of the alerts above (ADR-144).
            await alerts.SendAsync(
                WorkerAlerts.SourcePollFailed(source.SourceId, exception),
                cancellationToken);
        }
    }

    private async Task RecordFailureAsync(
        IServiceProvider services,
        ScheduleSource source,
        Exception failure,
        CancellationToken cancellationToken)
    {
        try
        {
            IScheduleSourceStore store = services.GetRequiredService<IScheduleSourceStore>();
            await store.RecordPollFailureAsync(
                source.SourceId,
                timeProvider.GetUtcNow(),
                failure.Message,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Reporting the failure must not become a second failure that stops the cycle: the
            // poll itself has already been logged, and the remaining sources still have to run.
            logger.LogError(
                exception,
                "The poll failure for source {SourceId} could not be recorded.",
                source.SourceId);
        }
    }
}
