using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Worker.Notifications;

namespace Sirkadiyen.Worker.Sources;

internal sealed class ScheduleDiffCalculationTask(
    IServiceScopeFactory scopeFactory,
    IOperatorAlertNotifier alerts,
    ILogger<ScheduleDiffCalculationTask> logger)
{
    private const int BatchSize = 10;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ScheduleDiffService diffs = scope.ServiceProvider
                .GetRequiredService<ScheduleDiffService>();
            ScheduleDiffCalculationBatch batch =
                await diffs.CalculatePendingAsync(BatchSize, cancellationToken);

            foreach (ScheduleDiffCalculationResult result in batch.Calculated)
            {
                LogResult(result);

                // Only a diff this pass actually stored is announced. A recalculation that lost
                // the race has already been announced by whichever pass won it (ADR-144).
                if (result.Outcome is ScheduleDiffPersistenceOutcome.Stored)
                {
                    await alerts.SendAsync(
                        WorkerAlerts.DiffCalculated(result.Diff),
                        cancellationToken);
                }
            }

            foreach (ScheduleDiffCalculationFailure failure in batch.Failed)
            {
                // A revision that cannot be diffed is surfaced by name rather than lost, because a
                // revision that never gets a diff has its deletions silently swallowed by the next
                // revision's baseline. Whether it will be tried again is the part that decides
                // whether an operator has to do something now (ADR-164).
                if (failure.DiffState is RevisionDiffState.Failed)
                {
                    logger.LogError(
                        "Revision {RevisionId} could not be diffed and has exhausted its attempts; "
                        + "it will not be retried automatically: {Reason}",
                        failure.RevisionId,
                        failure.Reason);
                }
                else
                {
                    logger.LogError(
                        "Revision {RevisionId} could not be diffed and will be retried after a "
                        + "back-off: {Reason}",
                        failure.RevisionId,
                        failure.Reason);
                }

                await alerts.SendAsync(
                    WorkerAlerts.DiffCalculationFailed(
                        failure.RevisionId,
                        failure.Reason,
                        failure.DiffState),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Calculating pending schedule diffs failed.");
            await alerts.SendAsync(
                WorkerAlerts.StageFailed("fark hesaplama", exception),
                cancellationToken);
        }
    }

    private void LogResult(ScheduleDiffCalculationResult result)
    {
        logger.LogInformation(
            "Revision {RevisionId} diff {Outcome} against {PreviousRevisionId}: "
            + "{CreatedCount} created, {UpdatedCount} updated, {DeletedCount} deleted, "
            + "{UnchangedCount} unchanged, {AmbiguousCount} ambiguous; state {DiffState}.",
            result.RevisionId,
            result.Outcome,
            result.Diff.PreviousRevisionId,
            result.Diff.CreatedCount,
            result.Diff.UpdatedCount,
            result.Diff.DeletedCount,
            result.Diff.UnchangedCount,
            result.Diff.AmbiguousCount,
            result.Diff.State);

        if (result.Diff.State is ScheduleDiffState.Held)
        {
            logger.LogWarning(
                "Diff {ScheduleDiffId} for revision {RevisionId} is held and will not "
                + "reach any calendar: {HoldReason} {ReleasableHint}",
                result.Diff.Id,
                result.RevisionId,
                result.Diff.HoldReason,
                result.Diff.IsReleasable
                    ? "Review it at GET /api/diffs/{id} and release it if the source is right."
                    : "It is ambiguous, so it can only be fixed at the source.");
        }
    }
}
