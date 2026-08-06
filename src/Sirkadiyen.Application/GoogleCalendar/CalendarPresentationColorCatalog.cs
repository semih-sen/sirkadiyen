namespace Sirkadiyen.Application.GoogleCalendar;

public static class CalendarPresentationColorCatalog
{
    public const string IntegratedSessionKey = "integrated-session";
    public const string PracticeKey = "practice";

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
