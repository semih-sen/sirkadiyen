using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Worker.Calendars;

internal sealed class CalendarInventoryTask(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarInventoryTask> logger)
{
    /// <returns>
    /// Whether the sweep yielded its Calendar budget with repairs outstanding, so the cycle
    /// should resume on the catch-up interval.
    /// </returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CalendarInventoryReconciliationService inventory = scope.ServiceProvider
                .GetRequiredService<CalendarInventoryReconciliationService>();
            CalendarInventoryRunResult result = await inventory.RunDueAsync(cancellationToken);
            if (result.Frozen)
            {
                logger.LogInformation(
                    "Calendar inventory reconciliation skipped because the global operational "
                    + "freeze is active.");
                return false;
            }

            foreach (CalendarInventoryUserResult user in result.Users)
            {
                LogResult(user);
            }

            if (result.CatchUpRequired)
            {
                logger.LogInformation(
                    "Calendar inventory spent its operation budget and yielded the Calendar "
                    + "fence; the sweep resumes on the next catch-up cycle.");
            }

            return result.CatchUpRequired;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running Calendar inventory reconciliation failed.");
            return false;
        }
    }

    private void LogResult(CalendarInventoryUserResult user)
    {
        if (user.Outcome is CalendarInventoryOutcome.Completed
            or CalendarInventoryOutcome.CompletedWithConflicts)
        {
            LogLevel level = user.Outcome is CalendarInventoryOutcome.Completed
                ? LogLevel.Information
                : LogLevel.Warning;
            logger.Log(
                level,
                "Calendar inventory completed for user {UserId}: {Inserted} inserted, "
                + "{Patched} patched, {MappingsRecovered} mappings recovered, "
                + "{LedgerRowsUpdated} ledger rows updated; {Conflicts} conflicts, "
                + "{UnexpectedMappings} unexpected mappings and {UnexpectedEvents} "
                + "unexpected events were preserved without deletion.",
                user.UserId, user.Inserted, user.Patched, user.MappingsRecovered,
                user.LedgerRowsUpdated, user.Conflicts, user.UnexpectedMappings,
                user.UnexpectedEvents);
            return;
        }

        switch (user.Outcome)
        {
            case CalendarInventoryOutcome.Frozen:
                logger.LogInformation(
                    "Calendar inventory was skipped for user {UserId} because their "
                    + "class/program pipeline is frozen.", user.UserId);
                break;
            case CalendarInventoryOutcome.AuthorizationRequired:
                logger.LogWarning(
                    "Calendar inventory stopped for user {UserId}; re-authorization is required.",
                    user.UserId);
                break;
            case CalendarInventoryOutcome.CalendarRepairRequired:
                logger.LogError(
                    "The managed calendar for user {UserId} is unavailable; normal writes are "
                    + "blocked and explicit repair is required.", user.UserId);
                break;
            case CalendarInventoryOutcome.Deferred:
                logger.LogWarning(
                    "Calendar inventory deferred for user {UserId}: {FailureReason}",
                    user.UserId, user.FailureReason);
                break;
            case CalendarInventoryOutcome.Failed:
            default:
                logger.LogError(
                    "Calendar inventory failed for user {UserId}: {FailureReason}",
                    user.UserId, user.FailureReason);
                break;
        }
    }
}
