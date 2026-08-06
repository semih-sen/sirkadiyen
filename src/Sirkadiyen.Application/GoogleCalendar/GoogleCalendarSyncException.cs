namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>Raised when a Calendar API call fails in a way synchronization cannot recover from.</summary>
/// <remarks>
/// This is the base of a small taxonomy the synchronization services branch on (ADR-059): a plain
/// instance is an unclassified terminal failure, <see cref="GoogleCalendarTransientException"/> is
/// worth a later retry, and <see cref="GoogleCalendarAuthorizationException"/> means the credential
/// itself is dead.
/// </remarks>
public class GoogleCalendarSyncException : Exception
{
    public GoogleCalendarSyncException(string message)
        : base(message)
    {
    }

    public GoogleCalendarSyncException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
