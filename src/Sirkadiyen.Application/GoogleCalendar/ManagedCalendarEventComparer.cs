namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Compares the visible Google representation and private markers with the deterministic
/// event Sirkadiyen expects. This detects user edits even when the content-hash marker was
/// left untouched.
/// </summary>
public static class ManagedCalendarEventComparer
{
    public static bool IsEquivalent(
        ManagedCalendarEvent expected,
        ManagedCalendarEventSnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (!string.Equals(expected.EventId, actual.EventId, StringComparison.Ordinal)
            || !TextEquals(expected.Summary, actual.Summary)
            || !TextEquals(expected.Description, actual.Description)
            || !TextEquals(expected.Location, actual.Location)
            || expected.IsAllDay != actual.IsAllDay
            || expected.PrivateProperties.Any(property =>
                !actual.PrivateProperties.TryGetValue(property.Key, out string? value)
                || !string.Equals(property.Value, value, StringComparison.Ordinal)))
        {
            return false;
        }

        if (expected.IsAllDay)
        {
            return expected.StartDate == actual.StartDate
                && expected.EndDateExclusive == actual.EndDateExclusive;
        }

        if (actual.StartAt is null || actual.EndAt is null)
        {
            return false;
        }

        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(expected.TimeZoneId);
        DateTimeOffset expectedStart = ToInstant(expected.LocalStart!.Value, zone);
        DateTimeOffset expectedEnd = ToInstant(expected.LocalEnd!.Value, zone);
        return expectedStart.UtcDateTime == actual.StartAt.Value.UtcDateTime
            && expectedEnd.UtcDateTime == actual.EndAt.Value.UtcDateTime;
    }

    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    private static bool TextEquals(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string? Normalize(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
