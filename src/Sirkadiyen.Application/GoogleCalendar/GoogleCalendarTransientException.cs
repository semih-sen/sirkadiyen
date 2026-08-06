namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Raised when a Calendar call failed transiently (rate limiting, a 5xx, or a network error) and did
/// not succeed within the client's bounded retry. The work is left for a later cycle to retry rather
/// than being treated as a permanent failure (ADR-059).
/// </summary>
public sealed class GoogleCalendarTransientException : GoogleCalendarSyncException
{
    public GoogleCalendarTransientException(string message)
        : base(message)
    {
    }

    public GoogleCalendarTransientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
