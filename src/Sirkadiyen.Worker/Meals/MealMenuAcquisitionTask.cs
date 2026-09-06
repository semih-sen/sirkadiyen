using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Meals;

namespace Sirkadiyen.Worker.Meals;

/// <summary>
/// Re-fetches the rolling cafeteria-menu window on its own cadence (ADR-150).
/// </summary>
/// <remarks>
/// Runs in the poll section of the worker cycle, not inside the Calendar fence: acquiring a menu is
/// a poll, like reading a schedule source, not a calendar mutation. It self-gates to the configured
/// interval and no-ops on every other cycle, the same way <see cref="Sources.ManualSourcePollTask"/>
/// is invoked every cycle but acts only when there is something to do. Two instances that poll the
/// same window at once is tolerated by the store rather than prevented here (a lost race just
/// re-fetches next cycle), so acquisition never has to hold the single-writer lease.
/// </remarks>
internal sealed class MealMenuAcquisitionTask(
    IServiceScopeFactory scopeFactory,
    MealMenuOptions options,
    TimeProvider timeProvider,
    ILogger<MealMenuAcquisitionTask> logger)
{
    private DateTimeOffset _nextDueAtUtc = DateTimeOffset.MinValue;

    /// <returns>Whether the menu changed, so a caller may converge calendars sooner.</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return false;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (now < _nextDueAtUtc)
        {
            return false;
        }

        _nextDueAtUtc = now + options.PollInterval;

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            MealMenuAcquisitionService service =
                scope.ServiceProvider.GetRequiredService<MealMenuAcquisitionService>();
            MealAcquisitionResult result = await service.AcquireAsync(cancellationToken);

            logger.LogInformation(
                "Meal acquisition for {WindowStart}..{WindowEnd}: {Published} new, {Changed} "
                + "changed, {Withdrawn} withdrawn, {Confirmed} confirmed, {Missed} missed, "
                + "{ApiErrors} API errors.",
                result.WindowStart,
                result.WindowEndInclusive,
                result.Published,
                result.ContentChanged,
                result.Withdrawn,
                result.Confirmed,
                result.Missed,
                result.ApiErrors);

            if (result.ApiErrors > 0)
            {
                logger.LogWarning(
                    "Meal acquisition could not read {ApiErrors} date(s); first: {FirstError}. "
                    + "These are not treated as missing menus.",
                    result.ApiErrors,
                    result.FirstApiError);
            }

            return result.Published + result.ContentChanged + result.Withdrawn > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Acquiring the cafeteria menu failed.");
            return false;
        }
    }
}
