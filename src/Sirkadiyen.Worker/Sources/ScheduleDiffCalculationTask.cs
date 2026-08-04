using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleDiffing;

namespace Sirkadiyen.Worker.Sources;

internal sealed class ScheduleDiffCalculationTask(
    IServiceScopeFactory scopeFactory,
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
            IReadOnlyList<ScheduleDiffCalculationResult> results =
                await diffs.CalculatePendingAsync(BatchSize, cancellationToken);

            foreach (ScheduleDiffCalculationResult result in results)
            {
                LogResult(result);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Calculating pending schedule diffs failed.");
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
