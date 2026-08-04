using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Worker.Calendars;

internal sealed class CalendarReconciliationTask(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarReconciliationTask> logger)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CalendarReconciliationService reconciliation = scope.ServiceProvider
                .GetRequiredService<CalendarReconciliationService>();
            CalendarReconciliationRunResult result =
                await reconciliation.RunPendingAsync(cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Calendar reconciliation skipped because the global operational freeze "
                    + "is active.");
                return false;
            }

            foreach (CalendarReconciliationUserResult user in result.Users)
            {
                LogResult(user);
            }

            return result.Users.Any(
                static user => user.Outcome is CalendarReconciliationOutcome.InProgress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running pending Calendar reconciliations failed.");
            return false;
        }
    }

    private void LogResult(CalendarReconciliationUserResult user)
    {
        switch (user.Outcome)
        {
            case CalendarReconciliationOutcome.Frozen:
                logger.LogInformation(
                    "Calendar reconciliation remains pending for user {UserId} because their "
                    + "class/program pipeline is frozen.", user.UserId);
                break;
            case CalendarReconciliationOutcome.Completed:
                logger.LogInformation(
                    "Calendar reconciliation completed for user {UserId}; no dispatched diff "
                    + "remains after the durable cursor.", user.UserId);
                break;
            case CalendarReconciliationOutcome.InProgress:
                logger.LogInformation(
                    "Calendar reconciliation advanced for user {UserId}: {DiffsReplayed} diffs, "
                    + "{Inserted} inserted, {Patched} patched, {Deleted} deleted.",
                    user.UserId, user.DiffsReplayed, user.Inserted, user.Patched, user.Deleted);
                break;
            case CalendarReconciliationOutcome.Deferred:
                logger.LogWarning(
                    "Calendar reconciliation deferred for user {UserId}; the cursor was preserved: "
                    + "{FailureReason}", user.UserId, user.FailureReason);
                break;
            case CalendarReconciliationOutcome.AuthorizationRequired:
                logger.LogWarning(
                    "Calendar reconciliation stopped for user {UserId} because the refreshed "
                    + "credential was rejected again; the cursor was preserved.", user.UserId);
                break;
            case CalendarReconciliationOutcome.Failed:
            default:
                logger.LogError(
                    "Calendar reconciliation failed for user {UserId}; the cursor was preserved: "
                    + "{FailureReason}", user.UserId, user.FailureReason);
                break;
        }
    }
}
