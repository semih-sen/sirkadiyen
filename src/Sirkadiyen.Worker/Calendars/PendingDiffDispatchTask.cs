using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Worker.Calendars;

internal sealed class PendingDiffDispatchTask(
    IServiceScopeFactory scopeFactory,
    ILogger<PendingDiffDispatchTask> logger)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IncrementalCalendarSyncService sync = scope.ServiceProvider
                .GetRequiredService<IncrementalCalendarSyncService>();
            IncrementalCalendarSyncRunResult result = await sync.RunPendingAsync(cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Incremental calendar dispatch skipped because the global operational "
                    + "freeze is active.");
                return false;
            }

            foreach (IncrementalCalendarSyncDiffResult diff in result.Diffs)
            {
                LogResult(diff);
            }

            return result.Diffs.Any(
                static diff => diff.Outcome is IncrementalDispatchOutcome.PartiallyDispatched);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Dispatching pending schedule diffs to calendars failed.");
            return false;
        }
    }

    private void LogResult(IncrementalCalendarSyncDiffResult diff)
    {
        switch (diff.Outcome)
        {
            case IncrementalDispatchOutcome.Frozen:
                logger.LogInformation(
                    "Diff {DiffId} remains pending because its class/program pipeline is frozen.",
                    diff.DiffId);
                break;
            case IncrementalDispatchOutcome.Dispatched:
                logger.LogInformation(
                    "Diff {DiffId} dispatched to calendars in {CalendarOperationsAttempted} "
                    + "mutation attempts: {Inserted} inserted, {Patched} patched, "
                    + "{Deleted} deleted; {ReauthFlagged} users flagged for re-authorization.",
                    diff.DiffId, diff.CalendarOperationsAttempted, diff.Inserted, diff.Patched,
                    diff.Deleted, diff.ReauthFlagged);
                break;
            case IncrementalDispatchOutcome.PartiallyDispatched:
                logger.LogInformation(
                    "Diff {DiffId} yielded after {CalendarOperationsAttempted} Calendar mutation "
                    + "attempts: {Inserted} inserted, {Patched} patched, {Deleted} deleted; "
                    + "{ReauthFlagged} users flagged for re-authorization. It remains pending.",
                    diff.DiffId, diff.CalendarOperationsAttempted, diff.Inserted, diff.Patched,
                    diff.Deleted, diff.ReauthFlagged);
                break;
            case IncrementalDispatchOutcome.Deferred:
                logger.LogWarning(
                    "Diff {DiffId} dispatch deferred after a transient failure and will retry: "
                    + "{FailureReason}", diff.DiffId, diff.FailureReason);
                break;
            case IncrementalDispatchOutcome.NoLongerPending:
                break;
            case IncrementalDispatchOutcome.Failed:
            default:
                logger.LogError(
                    "Diff {DiffId} dispatch failed after too many attempts and needs an operator: "
                    + "{FailureReason}", diff.DiffId, diff.FailureReason);
                break;
        }
    }
}
