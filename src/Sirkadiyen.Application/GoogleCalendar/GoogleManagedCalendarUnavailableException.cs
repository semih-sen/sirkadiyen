namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Raised when the attached dedicated calendar itself no longer exists or is inaccessible.
/// This is not a credential failure and must become an explicit repair-required state rather
/// than causing automatic calendar recreation.
/// </summary>
public sealed class GoogleManagedCalendarUnavailableException : GoogleCalendarSyncException
{
    public GoogleManagedCalendarUnavailableException(string message)
        : base(message)
    {
    }

    public GoogleManagedCalendarUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
