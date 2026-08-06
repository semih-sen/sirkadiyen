namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>Reads the single Calendar connection a user owns, without exposing any mutation.</summary>
/// <remarks>
/// The narrow read role every consumer shares: onboarding, authorization, the sync endpoints, and
/// the worker's sync services all need to look a connection up, but each only mutates it through the
/// role interface for its own workflow (<see cref="IUserCalendarConnectionStore"/> for user-driven
/// requests, <see cref="ICalendarSyncConnectionStore"/> for worker processing).
/// </remarks>
public interface IGoogleCalendarConnectionReader
{
    Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
