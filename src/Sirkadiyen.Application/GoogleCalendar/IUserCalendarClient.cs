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

/// <summary>Raised when a Calendar API call fails in a way synchronization cannot recover from.</summary>
public sealed class GoogleCalendarSyncException : Exception
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
