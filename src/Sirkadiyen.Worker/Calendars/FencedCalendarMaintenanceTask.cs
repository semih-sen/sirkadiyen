using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Worker.Calendars;

internal sealed class FencedCalendarMaintenanceTask(
    IServiceScopeFactory scopeFactory,
    PendingDiffDispatchTask dispatch,
    CalendarReconciliationTask reconciliation,
    ProfileAcademicYearDriftTask academicYearDrift,
    ProfileResyncTask profileResync,
    AnnouncementDispatchTask announcements,
    CalendarInventoryTask inventory,
    ILogger<FencedCalendarMaintenanceTask> logger)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ICalendarDispatchReconciliationFence fence = scope.ServiceProvider
                .GetRequiredService<ICalendarDispatchReconciliationFence>();
            await using IAsyncDisposable? lease = await fence.TryAcquireAsync(cancellationToken);
            if (lease is null)
            {
                logger.LogInformation(
                    "Calendar dispatch/reconciliation stage yielded because another worker "
                    + "holds the shared fence.");
                return false;
            }

            bool catchUpRequired = await dispatch.RunAsync(cancellationToken);
            catchUpRequired |= await reconciliation.RunAsync(cancellationToken);

            // Immediately before the resync it feeds. The reconciler restamps profiles whose
            // academic year the deployed schema has moved on from and flags their connections;
            // running it first means the requests it creates are converged in this cycle rather
            // than the next one (ADR-117).
            catchUpRequired |= await academicYearDrift.RunAsync(cancellationToken);

            // After replay, before inventory. Replay applies the diffs a user missed, which is
            // about the schedule; this converges who they are. Running it before inventory means
            // inventory sees the calendar the new profile expects rather than reporting the old
            // cohort's events as unexpected.
            catchUpRequired |= await profileResync.RunAsync(cancellationToken);

            // After every schedule stage: an announcement is the product speaking, and it must
            // never take the Calendar budget the schedule itself needs (ADR-107). Inventory still
            // runs afterwards and ignores announcement events, so it reports neither them nor a
            // conflict about them.
            catchUpRequired |= await announcements.RunAsync(cancellationToken);

            await inventory.RunAsync(cancellationToken);
            return catchUpRequired;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The fenced Calendar dispatch/reconciliation stage failed.");
            return false;
        }
    }
}
