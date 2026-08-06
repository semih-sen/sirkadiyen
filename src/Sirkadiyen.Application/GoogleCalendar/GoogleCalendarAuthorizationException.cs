namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>A Google authorization-code exchange that cannot yield a usable credential.</summary>
public sealed class GoogleCalendarAuthorizationException : Exception
{
    public GoogleCalendarAuthorizationException(string message)
        : base(message)
    {
    }

    public GoogleCalendarAuthorizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public GoogleCalendarAuthorizationException()
    {
    }
}
