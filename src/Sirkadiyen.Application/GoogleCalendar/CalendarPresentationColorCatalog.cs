namespace Sirkadiyen.Application.GoogleCalendar;

public static class CalendarPresentationColorCatalog
{
    public const string IntegratedSessionKey = "integrated-session";
    public const string PracticeKey = "practice";

    /// <summary>
    /// The cafeteria lunch menu's colour key (ADR-150). Equal to the meal event's label key
    /// (<c>meal:lunch</c>), so the colour the faculty or a student picks here is the one the menu
    /// event carries.
    /// </summary>
    public const string MealLunchKey = "meal:lunch";

    public static IReadOnlyList<CalendarPresentationColorDefinition> Categories { get; } =
    [
        new(
            IntegratedSessionKey,
            "Entegre oturumlar",
            "Birden fazla anabilim dalının birlikte yürüttüğü tüm oturumlar.",
            "#5E35B1"),
        new(
            PracticeKey,
            "Uygulamalar ve diseksiyonlar",
            "Dersinden bağımsız olarak tüm uygulama türleri ve diseksiyonlar.",
            "#FF6D00"),
        new(
            MealLunchKey,
            "Yemekhane öğle menüsü",
            "Fakülte yemekhanesinin takvime eklenen öğle yemeği menüsü.",
            "#2E7D32"),
    ];

    public static bool TryGet(
        string key,
        out CalendarPresentationColorDefinition definition)
    {
        definition = Categories.FirstOrDefault(
            item => StringComparer.Ordinal.Equals(item.Key, key))!;
        return definition is not null;
    }
}
