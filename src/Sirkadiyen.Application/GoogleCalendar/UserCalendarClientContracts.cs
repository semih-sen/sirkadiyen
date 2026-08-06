namespace Sirkadiyen.Application.GoogleCalendar;

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

    /// <summary>
    /// The calendar-scoped label that gives this event its department/category color.
    /// The infrastructure adapter ensures the label exists before writing the event.
    /// </summary>
    public required ManagedCalendarEventLabel Label { get; init; }

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

/// <summary>
/// One named, calendar-scoped Google event label. Unlike the legacy eleven-color
/// event palette, labels support enough distinct RGB colors for every department.
/// </summary>
public sealed record ManagedCalendarEventLabel
{
    /// <summary>A deterministic UUID, stable across users and synchronization runs.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>An RGB color in Google's required #RRGGBB form.</summary>
    public required string BackgroundColor { get; init; }
}

/// <summary>A read-only inventory projection of one Google Calendar event.</summary>
public sealed record ManagedCalendarEventSnapshot
{
    public required string EventId { get; init; }

    public string? Summary { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }

    public string? EventLabelId { get; init; }

    public required bool IsAllDay { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDateExclusive { get; init; }

    public DateTimeOffset? StartAt { get; init; }

    public DateTimeOffset? EndAt { get; init; }

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
