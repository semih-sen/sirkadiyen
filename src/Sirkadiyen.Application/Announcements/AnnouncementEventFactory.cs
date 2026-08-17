using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// Translates an announcement into the calendar event to write for one recipient (ADR-107). Pure
/// and deterministic, like its schedule counterpart, so the event id is the idempotency key.
/// </summary>
public static class AnnouncementEventFactory
{
    /// <summary>
    /// The namespaced identity an announcement event is keyed by, feeding the same derivation the
    /// schedule uses. The literal prefix is what keeps the two id spaces disjoint.
    /// </summary>
    public static string EventIdentity(Guid announcementId) =>
        $"announcement:{announcementId:N}";

    public static string DeterministicEventId(Guid userId, Guid announcementId) =>
        ManagedCalendarEventFactory.DeterministicEventId(userId, EventIdentity(announcementId));

    public static ManagedCalendarEvent ToManagedEvent(
        Guid userId,
        AnnouncementDispatchCandidate announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        AnnouncementCategory category =
            AnnouncementCategoryCatalog.Get(announcement.CategoryKey);

        // The kind marker is what lets inventory tell an announcement apart from a lesson. Without
        // it, a level-triggered scan comparing calendars against published truth would count every
        // announcement as an unexpected marked event and report a conflict on every pass.
        Dictionary<string, string> privateProperties = new(StringComparer.Ordinal)
        {
            [ManagedCalendarEventFactory.ManagedMarkerKey] = "1",
            [ManagedCalendarEventFactory.KindKey] = ManagedCalendarEventFactory.AnnouncementKind,
            ["announcementId"] = announcement.AnnouncementId.ToString("N"),
            ["contentVersion"] = announcement.ContentVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        };

        ManagedCalendarEvent managedEvent = new()
        {
            EventId = DeterministicEventId(userId, announcement.AnnouncementId),
            Summary = announcement.Title,
            Description = announcement.Body,
            Location = announcement.Location,
            Label = new ManagedCalendarEventLabel
            {
                Id = CalendarLabelId.For(category.Key),
                Name = category.Name,
                BackgroundColor = category.BackgroundColor,
            },
            TimeZoneId = announcement.TimeZoneId,
            IsAllDay = announcement.IsAllDay,
            ReminderMinutesBefore = announcement.ReminderMinutesBefore,
            PrivateProperties = privateProperties,
        };

        if (announcement.IsAllDay)
        {
            return managedEvent with
            {
                StartDate = announcement.LocalDate,
                EndDateExclusive = announcement.LocalDate.AddDays(1),
            };
        }

        // A timed announcement always carries both local times: the domain content type asserts it.
        return managedEvent with
        {
            LocalStart = announcement.LocalDate.ToDateTime(announcement.StartLocalTime!.Value),
            LocalEnd = announcement.LocalDate.ToDateTime(announcement.EndLocalTime!.Value),
        };
    }
}
