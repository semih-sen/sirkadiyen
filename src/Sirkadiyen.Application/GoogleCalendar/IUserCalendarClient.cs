namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Writes to a user's Google Calendar on their behalf. Implemented in the infrastructure
/// layer over the Calendar API; abstracted here so the synchronization use cases stay free
/// of any Google dependency and can be tested with a fake (ADR-057, ADR-058).
/// </summary>
public interface IUserCalendarClient
{
    /// <summary>
    /// Creates the dedicated Sirkadiyen calendar for a user and returns its id (ADR-024).
    /// </summary>
    /// <remarks>
    /// Calendar creation is the one step that is not naturally idempotent: calling it twice
    /// makes two calendars. The caller creates a calendar only when the connection has none,
    /// and persists the returned id immediately, so this is invoked at most once per user in
    /// the normal path.
    /// </remarks>
    Task<string> CreateManagedCalendarAsync(
        CalendarAccess access,
        string calendarSummary,
        string timeZoneId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts one managed event into the calendar. The event carries a client-chosen id,
    /// so a re-insert of the same id is reported as <see cref="CalendarEventInsertOutcome.AlreadyExists"/>
    /// rather than creating a duplicate — the idempotency key the initial sync relies on.
    /// </summary>
    Task<CalendarEventInsertOutcome> InsertEventAsync(
        CalendarAccess access,
        string calendarId,
        ManagedCalendarEvent calendarEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates one existing managed event to match a newer canonical record (ADR-059). Keyed by the
    /// deterministic event id the event was written with, so a patch is idempotent. An event that no
    /// longer exists is reported as <see cref="CalendarEventPatchOutcome.NotFound"/> rather than
    /// failing, which lets a resumed dispatch tolerate a delete that already happened.
    /// </summary>
    Task<CalendarEventPatchOutcome> PatchEventAsync(
        CalendarAccess access,
        string calendarId,
        ManagedCalendarEvent calendarEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes one managed event by its deterministic id (ADR-059). Deleting an event that is already
    /// gone is reported as <see cref="CalendarEventDeleteOutcome.NotFound"/> rather than failing, so a
    /// resumed dispatch converges.
    /// </summary>
    Task<CalendarEventDeleteOutcome> DeleteEventAsync(
        CalendarAccess access,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken);
}

/// <summary>
/// The credential needed to act on one user's calendar. Holds the plaintext refresh token,
/// which lives only in memory for the duration of a synchronization call.
/// </summary>
public sealed record CalendarAccess
{
    public required string RefreshToken { get; init; }
}

/// <summary>
/// A calendar event to write, expressed without any Google type so the translation from a
/// canonical record stays a pure, testable function.
/// </summary>
public sealed record ManagedCalendarEvent
{
    /// <summary>The deterministic, client-chosen event id used for idempotency.</summary>
    public required string EventId { get; init; }

    public required string Summary { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }

    /// <summary>The IANA time zone the local times are expressed in.</summary>
    public required string TimeZoneId { get; init; }

    public required bool IsAllDay { get; init; }

    /// <summary>The local wall-clock start of a timed event; null for an all-day item.</summary>
    public DateTime? LocalStart { get; init; }

    /// <summary>The local wall-clock end of a timed event; null for an all-day item.</summary>
    public DateTime? LocalEnd { get; init; }

    /// <summary>The first day of an all-day item; null for a timed event.</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>
    /// The exclusive end day of an all-day item; null for a timed event. Google Calendar
    /// treats the end date as exclusive, so a single-day closure ends on the following day.
    /// </summary>
    public DateOnly? EndDateExclusive { get; init; }

    /// <summary>
    /// Private extended properties marking the event as Sirkadiyen-managed and carrying the
    /// identity and content it was written from (ADR-024). Only Sirkadiyen can read them.
    /// </summary>
    public required IReadOnlyDictionary<string, string> PrivateProperties { get; init; }
}

public enum CalendarEventInsertOutcome
{
    /// <summary>The event was created.</summary>
    Inserted,

    /// <summary>An event with this id already existed; the insert was a safe no-op.</summary>
    AlreadyExists,
}

public enum CalendarEventPatchOutcome
{
    /// <summary>The event was updated.</summary>
    Patched,

    /// <summary>No event with this id existed to update; the patch was a safe no-op.</summary>
    NotFound,
}

public enum CalendarEventDeleteOutcome
{
    /// <summary>The event was removed.</summary>
    Deleted,

    /// <summary>No event with this id existed; the delete was a safe no-op.</summary>
    NotFound,
}

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
