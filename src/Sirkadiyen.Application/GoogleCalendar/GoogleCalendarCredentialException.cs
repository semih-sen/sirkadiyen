namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Raised when Google rejected the stored credential during a synchronization write (a revoked grant
/// or expired refresh token): the connection must be flagged for re-authorization, and this user
/// skipped, without touching what was already written or blocking other users (ADR-059). Distinct
/// from <see cref="GoogleCalendarAuthorizationException"/>, which is the authorization-code exchange
/// failing at grant time (ADR-057).
/// </summary>
public sealed class GoogleCalendarCredentialException : GoogleCalendarSyncException
{
    public GoogleCalendarCredentialException(string message)
        : base(message)
    {
    }

    public GoogleCalendarCredentialException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
