namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// A shared cross-process fence held continuously from global diff dispatch through semantic
/// replay's empty scan and the inventory sweep. This closes the multi-worker window where one
/// worker could complete replay while another still had an in-flight dispatch that skipped
/// that user.
/// </summary>
public interface ICalendarDispatchReconciliationFence
{
    /// <summary>
    /// Attempts to acquire the singleton fence without waiting. Null means another worker owns
    /// the stage and this cycle should yield.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}
