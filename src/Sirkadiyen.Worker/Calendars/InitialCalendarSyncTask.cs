using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Worker.Calendars;

internal sealed class InitialCalendarSyncTask(
    IServiceScopeFactory scopeFactory,
    ILogger<InitialCalendarSyncTask> logger)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            InitialCalendarSyncService sync = scope.ServiceProvider
                .GetRequiredService<InitialCalendarSyncService>();
            InitialCalendarSyncRunResult result = await sync.RunPendingAsync(cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Initial calendar synchronization skipped because the global operational "
                    + "freeze is active.");
                return false;
            }

            foreach (InitialCalendarSyncResult user in result.Users)
            {
                LogResult(user);
            }

            return result.Users.Any(
                static user => user.Outcome is InitialCalendarSyncOutcome.InProgress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running pending initial calendar syncs failed.");
            return false;
        }
    }

    private void LogResult(InitialCalendarSyncResult user)
    {
        switch (user.Outcome)
        {
            case InitialCalendarSyncOutcome.Frozen:
                logger.LogInformation(
                    "Initial calendar sync remains pending for user {UserId} because their "
                    + "class/program pipeline is frozen.", user.UserId);
                break;
            case InitialCalendarSyncOutcome.Completed:
                logger.LogInformation(
                    "Initial calendar sync completed for user {UserId}; wrote {EventsWritten} "
                    + "events this cycle of {ApplicableRecordCount} applicable.",
                    user.UserId, user.EventsWritten, user.ApplicableRecordCount);
                break;
            case InitialCalendarSyncOutcome.InProgress:
                logger.LogInformation(
                    "Initial calendar sync advanced for user {UserId}; wrote {EventsWritten} "
                    + "events this cycle, more remain of {ApplicableRecordCount} applicable.",
                    user.UserId, user.EventsWritten, user.ApplicableRecordCount);
                break;
            case InitialCalendarSyncOutcome.ProfileMissing:
                logger.LogWarning(
                    "Initial calendar sync could not run for user {UserId}: no student profile "
                    + "was found, so nothing could be resolved.", user.UserId);
                break;
            case InitialCalendarSyncOutcome.AuthorizationRequired:
                logger.LogWarning(
                    "Initial calendar sync stopped for user {UserId}: the Calendar grant is "
                    + "missing a required scope or was revoked, so re-authorization is required.",
                    user.UserId);
                break;
            case InitialCalendarSyncOutcome.Failed:
            default:
                logger.LogError(
                    "Initial calendar sync failed for user {UserId} and will retry next cycle: "
                    + "{FailureReason}", user.UserId, user.FailureReason);
                break;
        }
    }
}
