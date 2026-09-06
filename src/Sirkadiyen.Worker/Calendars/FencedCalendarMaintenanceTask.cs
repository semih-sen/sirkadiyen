using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Worker.Meals;

namespace Sirkadiyen.Worker.Calendars;

internal sealed class FencedCalendarMaintenanceTask(
    IServiceScopeFactory scopeFactory,
    InitialCalendarSyncTask initialSync,
    PendingDiffDispatchTask dispatch,
    CalendarReconciliationTask reconciliation,
    ProfileAcademicYearDriftTask academicYearDrift,
    ProfileResyncTask profileResync,
    AnnouncementDispatchTask announcements,
    MealDeliveryTask meals,
    CalendarInventoryTask inventory,
    ILogger<FencedCalendarMaintenanceTask> logger)
{
    /// <param name="mealMenuChanged">
    /// Whether this cycle's acquisition changed the menu, so the meal convergence runs now instead
    /// of waiting for its interval.
    /// </param>
    public async Task<bool> RunAsync(bool mealMenuChanged, CancellationToken cancellationToken)
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
                    "Calendar stage yielded because another worker holds the shared fence.");
                return false;
            }

            // Initial sync is fenced first, before every other calendar stage (ADR-122). It creates
            // each user's dedicated calendar and writes their events, and its calendar creation is a
            // check-then-act step: two worker instances that both picked up the same pending user
            // could each create a calendar and split the user's events between them, leaving the
            // ledger describing events that are not on the calendar the connection points at. Holding
            // the same session advisory lock the other stages use makes exactly one worker perform
            // calendar work at a time, which is the only safe way to run a non-idempotent create.
            bool catchUpRequired = await initialSync.RunAsync(cancellationToken);
            catchUpRequired |= await dispatch.RunAsync(cancellationToken);
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

            // Last of the write stages, for the same reason as announcements: the cafeteria menu is
            // the product speaking, not schedule truth, so it yields the Calendar budget to
            // everything above it (ADR-150). Inventory, which runs next, ignores its events too.
            catchUpRequired |= await meals.RunAsync(mealMenuChanged, cancellationToken);

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
