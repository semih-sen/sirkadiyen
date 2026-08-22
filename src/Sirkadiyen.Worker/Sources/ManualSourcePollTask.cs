using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Worker.Sources;

/// <summary>
/// Drains operator-requested source polls and executes them immediately (ADR-127).
/// </summary>
/// <remarks>
/// Runs every worker cycle, independently of the adaptive poll schedule, so a manual "poll now"
/// takes effect within the idle-check interval rather than waiting up to an hour for the next
/// scheduled poll. Each claimed request is polled through the same <see cref="ScheduleSourcePoller"/>
/// the scheduled path uses, honouring its force flag so an unchanged document can be reparsed.
/// </remarks>
internal sealed class ManualSourcePollTask(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ManualSourcePollTask> logger)
{
    private const int ClaimBatchSize = 20;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Polling is not covered by the calendar fence, so a frozen program must not be polled here
        // either. Checked before claiming, so a request left unclaimed is simply picked up once the
        // freeze lifts rather than consumed and dropped.
        await using AsyncServiceScope claimScope = scopeFactory.CreateAsyncScope();
        if (await IsFrozenAsync(claimScope.ServiceProvider, cancellationToken))
        {
            return;
        }

        ISourcePollRequestStore requests = claimScope.ServiceProvider
            .GetRequiredService<ISourcePollRequestStore>();
        IReadOnlyList<SourcePollRequest> claimed = await requests.ClaimPendingAsync(
            ClaimBatchSize,
            timeProvider.GetUtcNow(),
            cancellationToken);

        foreach (SourcePollRequest request in claimed)
        {
            await PollAsync(request, cancellationToken);
        }
    }

    private async Task PollAsync(SourcePollRequest request, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        try
        {
            IScheduleSourceStore sources = scope.ServiceProvider
                .GetRequiredService<IScheduleSourceStore>();
            ScheduleSource? source = await sources.FindAsync(request.SourceId, cancellationToken);
            if (source is null)
            {
                logger.LogWarning(
                    "Manual poll request for unknown source {SourceId} was skipped.",
                    request.SourceId);
                return;
            }

            ScheduleSourcePoller poller = scope.ServiceProvider
                .GetRequiredService<ScheduleSourcePoller>();
            ScheduleSourcePollResult result = await poller.PollAsync(
                source,
                request.Force,
                cancellationToken);
            logger.LogInformation(
                "Manual poll of {SourceId} (force: {Force}) by {RequestedBy} completed with "
                + "{Outcome}; revision: {RevisionId}.",
                result.SourceId,
                request.Force,
                request.RequestedBy,
                result.Outcome,
                result.RevisionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Manual poll of source {SourceId} failed.",
                request.SourceId);
        }
    }

    private async Task<bool> IsFrozenAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        try
        {
            IOperationalFreezeStore freeze = services.GetRequiredService<IOperationalFreezeStore>();
            OperationalFreezeSnapshot state = await freeze.GetAsync(cancellationToken);
            if (state.IsFrozen)
            {
                logger.LogWarning("Source polling is frozen; manual poll requests are held.");
                return true;
            }

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
                "The operational freeze state could not be read; manual polls are held this cycle.");
            return true;
        }
    }
}
