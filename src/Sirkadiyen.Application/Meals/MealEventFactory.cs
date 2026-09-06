using System.Globalization;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.Meals;

namespace Sirkadiyen.Application.Meals;

/// <summary>
/// Translates an acquired menu-day into the calendar event to write for one subscriber (ADR-150).
/// Pure and deterministic, like its schedule and announcement counterparts, so the event id is the
/// idempotency key.
/// </summary>
public static class MealEventFactory
{
    /// <summary>
    /// The namespaced identity a meal event is keyed by, feeding the same derivation the schedule
    /// and announcements use. The literal <c>meal:</c> prefix keeps the id space disjoint from a
    /// lesson's hex stable identity and from an announcement's <c>announcement:</c> identity.
    /// </summary>
    public static string EventIdentity(DateOnly localDate, MealCategory category) =>
        $"meal:{CategorySlug(category)}:{localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public static string DeterministicEventId(Guid userId, DateOnly localDate, MealCategory category) =>
        ManagedCalendarEventFactory.DeterministicEventId(userId, EventIdentity(localDate, category));

    /// <param name="categoryColors">
    /// The viewer's effective calendar palette (ADR-072), keyed by colour key. When it carries the
    /// meal category's key the menu takes that colour — so a colour picked in the faculty palette
    /// wins over the catalogue default. Null (or a missing key) falls back to the catalogue colour.
    /// </param>
    public static ManagedCalendarEvent ToManagedEvent(
        Guid userId,
        MealMenuDayContent day,
        MealEventPresentation presentation,
        IReadOnlyDictionary<string, string>? categoryColors = null)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(presentation);
        presentation.Validate();

        MealCategoryPresentation category = MealCategoryCatalog.Get(day.Category);
        string backgroundColor =
            categoryColors?.GetValueOrDefault(category.Key) ?? category.BackgroundColor;

        // The kind marker is what lets calendar inventory tell a menu apart from a lesson. Without
        // it, a level-triggered scan comparing calendars against published schedule truth would
        // count every menu as an unexpected marked event and report a conflict on every pass.
        Dictionary<string, string> privateProperties = new(StringComparer.Ordinal)
        {
            [ManagedCalendarEventFactory.ManagedMarkerKey] = "1",
            [ManagedCalendarEventFactory.KindKey] = ManagedCalendarEventFactory.MealKind,
            ["mealDate"] = day.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["mealCategory"] = CategorySlug(day.Category),
            ["contentVersion"] = day.ContentVersion.ToString(CultureInfo.InvariantCulture),
        };

        return new ManagedCalendarEvent
        {
            EventId = DeterministicEventId(userId, day.LocalDate, day.Category),
            Summary = $"🍽️ {category.Name}",
            Description = day.MealText,
            Location = presentation.Location,
            Label = new ManagedCalendarEventLabel
            {
                Id = CalendarLabelId.For(category.Key),
                Name = category.Name,
                BackgroundColor = backgroundColor,
            },
            TimeZoneId = presentation.TimeZoneId,
            IsAllDay = false,
            LocalStart = day.LocalDate.ToDateTime(presentation.StartLocalTime),
            LocalEnd = day.LocalDate.ToDateTime(presentation.EndLocalTime),
            ReminderMinutesBefore = presentation.ReminderMinutesBefore,
            PrivateProperties = privateProperties,
        };
    }

    private static string CategorySlug(MealCategory category) => category switch
    {
        MealCategory.Breakfast => "breakfast",
        MealCategory.Lunch => "lunch",
        MealCategory.Dinner => "dinner",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown meal category."),
    };
}

/// <summary>The menu content one event is written from.</summary>
public sealed record MealMenuDayContent
{
    public required DateOnly LocalDate { get; init; }

    public required MealCategory Category { get; init; }

    public required string MealText { get; init; }

    public required int ContentVersion { get; init; }
}

/// <summary>How and when a meal event sits on the calendar; configuration, not per-day data.</summary>
public sealed record MealEventPresentation
{
    public required TimeOnly StartLocalTime { get; init; }

    public required TimeOnly EndLocalTime { get; init; }

    public required string TimeZoneId { get; init; }

    public string? Location { get; init; }

    /// <summary>Minutes before the start to remind, or null to leave the calendar default.</summary>
    public int? ReminderMinutesBefore { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TimeZoneId);
        if (EndLocalTime <= StartLocalTime)
        {
            throw new ArgumentException("A meal event must end after it starts.", nameof(EndLocalTime));
        }

        if (ReminderMinutesBefore is { } reminder)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(reminder, nameof(ReminderMinutesBefore));
        }
    }
}
