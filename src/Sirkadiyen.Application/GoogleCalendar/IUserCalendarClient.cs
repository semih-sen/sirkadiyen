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
        string descriptionMarker,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds app-created calendars carrying the exact per-user marker. A single result can be
    /// safely reattached after a creation/persistence crash; zero means create, and more than
    /// one is an operator-visible conflict.
    /// </summary>
    Task<IReadOnlyList<string>> FindManagedCalendarIdsAsync(
        CalendarAccess access,
        string descriptionMarker,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates only Sirkadiyen-managed events on the attached calendar, including their
    /// private markers and visible content for inventory reconciliation.
    /// </summary>
    Task<IReadOnlyList<ManagedCalendarEventSnapshot>> ListManagedEventsAsync(
        CalendarAccess access,
        string calendarId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates one calendar-scoped event label. Inventory calls this even when
    /// an event already has the same label id, because its configured RGB color may change.
    /// </summary>
    Task EnsureEventLabelAsync(
        CalendarAccess access,
        string calendarId,
        ManagedCalendarEventLabel label,
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

    /// <summary>
    /// Deletes the whole dedicated Sirkadiyen calendar container and every event on it (ADR-118).
    /// Used only when an account is deleted, so the calendar the product created does not survive
    /// its owner.
    /// </summary>
    /// <remarks>
    /// This is a best-effort courtesy, not a synchronization primitive: account deletion proceeds
    /// even if this fails (a dead token, a calendar the user already removed), so the outcome is
    /// reported rather than thrown. A calendar that is already gone is reported as
    /// <see cref="CalendarContainerDeleteOutcome.NotFound"/>.
    /// <para>
    /// It deletes an entire calendar, which no synchronization path ever does — every other write
    /// here operates on a single event by its deterministic id (AI_GUIDELINE §13). The authority is
    /// the account owner's own erasure request, or an operator's audited one, never a diff.
    /// </para>
    /// </remarks>
    Task<CalendarContainerDeleteOutcome> DeleteManagedCalendarAsync(
        CalendarAccess access,
        string calendarId,
        CancellationToken cancellationToken);
}
