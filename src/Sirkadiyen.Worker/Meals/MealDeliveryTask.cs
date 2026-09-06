using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Meals;

namespace Sirkadiyen.Worker.Meals;

/// <summary>
/// Converges subscribers' calendars to the current cafeteria menu (ADR-150). Runs inside the shared
/// Calendar fence, last, after every schedule stage and the announcements.
/// </summary>
/// <remarks>
/// Last on purpose, for the same reason announcements are: the schedule is what students depend on,
/// so the menu must never take the per-cycle Calendar budget that diff dispatch, replay and profile
/// convergence need first. It self-gates to the delivery interval so the convergence queries do not
/// run on every idle Calendar cycle, and runs immediately when acquisition reports a change.
/// </remarks>
internal sealed class MealDeliveryTask(
    IServiceScopeFactory scopeFactory,
    MealMenuOptions options,
    TimeProvider timeProvider,
    ILogger<MealDeliveryTask> logger)
{
    private DateTimeOffset _nextDueAtUtc = DateTimeOffset.MinValue;

    /// <param name="force">Run now regardless of the interval — used when the menu just changed.</param>
    /// <returns>Whether work remains, so the scheduler shortens the next cycle.</returns>
    public async Task<bool> RunAsync(bool force, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return false;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (!force && now < _nextDueAtUtc)
        {
            return false;
        }

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            MealDeliveryService service =
                scope.ServiceProvider.GetRequiredService<MealDeliveryService>();
            MealDeliveryRunResult result = await service.RunAsync(cancellationToken);

            if (result.Frozen)
            {
                logger.LogInformation(
                    "Meal delivery skipped because the global operational freeze is active.");
                // Re-check on the normal cadence rather than hammering the freeze while it holds.
                _nextDueAtUtc = now + options.DeliveryInterval;
                return false;
            }

            LogResult(result);

            // When work remains, come back next cycle to drain it; otherwise wait the interval.
            _nextDueAtUtc = result.WorkRemains ? now : now + options.DeliveryInterval;
            return result.WorkRemains;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Converging cafeteria menu events failed.");
            _nextDueAtUtc = now + options.DeliveryInterval;
            return false;
        }
    }

    private void LogResult(MealDeliveryRunResult result)
    {
        if (result.Deferred)
        {
            logger.LogWarning(
                "Meal delivery deferred after a transient Calendar failure and will retry: "
                + "{FailureReason}",
                result.FailureReason);
        }

        if (result.EventsWritten + result.EventsPatched + result.EventsRemoved
            + result.SubscribersSkipped + result.DeliveriesFailed > 0)
        {
            logger.LogInformation(
                "Meal delivery: wrote {Written}, patched {Patched}, removed {Removed}, skipped "
                + "{Skipped}, failed {Failed}.",
                result.EventsWritten,
                result.EventsPatched,
                result.EventsRemoved,
                result.SubscribersSkipped,
                result.DeliveriesFailed);
        }
    }
}
