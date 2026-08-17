namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// The fixed set of categories an announcement may be published under, and the calendar colour
/// each one carries (ADR-107).
/// </summary>
/// <remarks>
/// Server-owned rather than an operator-typed colour, for the same reason department colours are
/// (ADR-072): the label is calendar-scoped and shared by every event under the key, so a free-form
/// colour would let one announcement silently recolour another. The palette deliberately avoids
/// the schedule categories' colours, so a message from Sirkadiyen never looks like a lesson.
/// </remarks>
public static class AnnouncementCategoryCatalog
{
    private static readonly IReadOnlyList<AnnouncementCategory> All =
    [
        new("announcement:notice", "Sirkadiyen duyurusu", "#00838F"),
        new("announcement:academic", "Akademik bilgilendirme", "#1A73E8"),
        new("announcement:warning", "Sirkadiyen uyarısı", "#D81B60"),
        new("announcement:maintenance", "Planlı bakım", "#795548"),
    ];

    /// <summary>The default an operator gets before choosing anything.</summary>
    public const string DefaultKey = "announcement:notice";

    public static IReadOnlyList<AnnouncementCategory> List() => All;

    public static bool IsKnown(string categoryKey) =>
        All.Any(category => string.Equals(category.Key, categoryKey, StringComparison.Ordinal));

    /// <summary>
    /// The category for a key. An unknown key throws rather than falling back silently: a colour
    /// chosen by accident is a colour nobody decided, and the write path validates the key first.
    /// </summary>
    public static AnnouncementCategory Get(string categoryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);

        return All.FirstOrDefault(category =>
                string.Equals(category.Key, categoryKey, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(
                nameof(categoryKey),
                categoryKey,
                "Unknown announcement category.");
    }
}

/// <summary>One announcement category: its stable key, its label name and its colour.</summary>
public sealed record AnnouncementCategory(string Key, string Name, string BackgroundColor);
