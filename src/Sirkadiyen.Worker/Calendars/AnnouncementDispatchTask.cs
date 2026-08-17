using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Announcements;

namespace Sirkadiyen.Worker.Calendars;

/// <summary>
/// Writes queued administrator announcements onto recipients' calendars, and removes cancelled
/// ones (ADR-107). Runs inside the shared Calendar fence, last, after every schedule stage.
/// </summary>
/// <remarks>
/// Last on purpose. The schedule is what students depend on, so a large announcement campaign must
/// never consume the per-cycle Calendar budget that diff dispatch, replay and profile convergence
/// need first.
/// </remarks>
internal sealed class AnnouncementDispatchTask(
    IServiceScopeFactory scopeFactory,
    ILogger<AnnouncementDispatchTask> logger)
{
    /// <returns>Whether work remains, so the scheduler shortens the next cycle.</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            AnnouncementDispatchService dispatch = scope.ServiceProvider
                .GetRequiredService<AnnouncementDispatchService>();
            AnnouncementDispatchRunResult result =
                await dispatch.RunPendingAsync(cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Announcement delivery skipped because the global operational freeze is "
                    + "active.");
                return false;
            }

            foreach (AnnouncementDispatchResult announcement in result.Announcements)
            {
                LogResult(announcement);
            }

            return result.Announcements.Any(static announcement =>
                announcement.Outcome is AnnouncementDispatchOutcome.InProgress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Delivering pending calendar announcements failed.");
            return false;
        }
    }

    private void LogResult(AnnouncementDispatchResult announcement)
    {
        switch (announcement.Outcome)
        {
            case AnnouncementDispatchOutcome.Completed:
                logger.LogInformation(
                    "Announcement {AnnouncementId} delivered: wrote {EventsWritten}, patched "
                    + "{EventsPatched}, skipped {RecipientsSkipped}, failed {DeliveriesFailed}.",
                    announcement.AnnouncementId, announcement.EventsWritten,
                    announcement.EventsPatched, announcement.RecipientsSkipped,
                    announcement.DeliveriesFailed);
                break;
            case AnnouncementDispatchOutcome.InProgress:
                logger.LogInformation(
                    "Announcement {AnnouncementId} advanced: wrote {EventsWritten}, patched "
                    + "{EventsPatched}, skipped {RecipientsSkipped}; {RecipientsDeferred} "
                    + "recipients remain queued and more work follows next cycle.",
                    announcement.AnnouncementId, announcement.EventsWritten,
                    announcement.EventsPatched, announcement.RecipientsSkipped,
                    announcement.RecipientsDeferred);
                break;
            case AnnouncementDispatchOutcome.Cancelled:
                logger.LogInformation(
                    "Announcement {AnnouncementId} cancelled: removed {EventsRemoved} events, "
                    + "{DeliveriesFailed} could not be removed.",
                    announcement.AnnouncementId, announcement.EventsRemoved,
                    announcement.DeliveriesFailed);
                break;
            case AnnouncementDispatchOutcome.Deferred:
                logger.LogWarning(
                    "Announcement {AnnouncementId} deferred after a transient Calendar failure "
                    + "and will retry: {FailureReason}",
                    announcement.AnnouncementId, announcement.FailureReason);
                break;
            case AnnouncementDispatchOutcome.Failed:
            default:
                logger.LogError(
                    "Announcement {AnnouncementId} stopped after reaching the delivery attempt "
                    + "cap; an operator has to look at it: {FailureReason}",
                    announcement.AnnouncementId, announcement.FailureReason);
                break;
        }
    }
}
