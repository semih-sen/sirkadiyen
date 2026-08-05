using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Worker.Calendars;

/// <summary>
/// Converges the calendars of students whose academic profile changed the audience they resolve
/// (ADR-096). Runs inside the shared Calendar fence, after replay and before inventory.
/// </summary>
internal sealed class ProfileResyncTask(
    IServiceScopeFactory scopeFactory,
    ILogger<ProfileResyncTask> logger)
{
    /// <returns>Whether work remains, so the scheduler shortens the next cycle.</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ProfileChangeResyncService resync = scope.ServiceProvider
                .GetRequiredService<ProfileChangeResyncService>();
            ProfileResyncRunResult result = await resync.RunPendingAsync(cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Profile re-synchronization skipped because the global operational freeze "
                    + "is active.");
                return false;
            }

            foreach (ProfileResyncResult user in result.Users)
            {
                LogResult(user);
            }

            return result.Users.Any(static user =>
                user.Outcome is ProfileResyncOutcome.InProgress
                    or ProfileResyncOutcome.Superseded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running pending profile re-synchronizations failed.");
            return false;
        }
    }

    private void LogResult(ProfileResyncResult user)
    {
        switch (user.Outcome)
        {
            case ProfileResyncOutcome.Frozen:
                logger.LogInformation(
                    "Profile re-synchronization remains pending for user {UserId} because their "
                    + "class/program pipeline is frozen.", user.UserId);
                break;
            case ProfileResyncOutcome.Completed:
                logger.LogInformation(
                    "Profile re-synchronization completed for user {UserId}; removed "
                    + "{EventsRemoved} and wrote {EventsWritten} events this cycle of "
                    + "{ApplicableRecordCount} applicable.",
                    user.UserId, user.EventsRemoved, user.EventsWritten,
                    user.ApplicableRecordCount);
                break;
            case ProfileResyncOutcome.Superseded:
                logger.LogInformation(
                    "Profile re-synchronization converged user {UserId} (removed "
                    + "{EventsRemoved}, wrote {EventsWritten}), but their profile changed again "
                    + "during the pass, so a newer request remains pending.",
                    user.UserId, user.EventsRemoved, user.EventsWritten);
                break;
            case ProfileResyncOutcome.InProgress:
                logger.LogInformation(
                    "Profile re-synchronization advanced for user {UserId}; removed "
                    + "{EventsRemoved} and wrote {EventsWritten} events this cycle, more remain "
                    + "of {ApplicableRecordCount} applicable.",
                    user.UserId, user.EventsRemoved, user.EventsWritten,
                    user.ApplicableRecordCount);
                break;
            case ProfileResyncOutcome.ProfileMissing:
                logger.LogWarning(
                    "Profile re-synchronization could not run for user {UserId}: no student "
                    + "profile was found, so nothing could be resolved.", user.UserId);
                break;
            case ProfileResyncOutcome.AuthorizationRequired:
                logger.LogWarning(
                    "Profile re-synchronization stopped for user {UserId}: their Calendar grant "
                    + "was revoked or expired, so the request waits for re-authorization.",
                    user.UserId);
                break;
            case ProfileResyncOutcome.CalendarRepairRequired:
                logger.LogWarning(
                    "Profile re-synchronization stopped for user {UserId}: their managed calendar "
                    + "is missing or inaccessible. {FailureReason}",
                    user.UserId, user.FailureReason);
                break;
            case ProfileResyncOutcome.Failed:
            default:
                logger.LogError(
                    "Profile re-synchronization failed for user {UserId} and will retry next "
                    + "cycle: {FailureReason}", user.UserId, user.FailureReason);
                break;
        }
    }
}
