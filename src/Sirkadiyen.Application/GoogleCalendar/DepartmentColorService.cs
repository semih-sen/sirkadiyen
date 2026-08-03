using System.Text.RegularExpressions;

namespace Sirkadiyen.Application.GoogleCalendar;

public interface IDepartmentColorStore
{
    Task<IReadOnlyDictionary<string, string>> GetAdminDefaultsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> GetUserOverridesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> SetAdminDefaultAsync(
        string departmentKey,
        string? color,
        string actor,
        string reason,
        string correlationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task<bool> SetUserOverrideAsync(
        Guid userId,
        string departmentKey,
        string? color,
        string correlationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

public sealed partial class DepartmentColorService(
    IDepartmentColorStore store,
    TimeProvider timeProvider)
{
    private readonly Dictionary<Guid, IReadOnlyDictionary<string, string>> effectiveCache = [];

    public async Task<IReadOnlyList<DepartmentColorView>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> admin =
            await store.GetAdminDefaultsAsync(cancellationToken);
        IReadOnlyDictionary<string, string> user =
            await store.GetUserOverridesAsync(userId, cancellationToken);
        return Views(admin, user);
    }

    public async Task<IReadOnlyList<DepartmentColorView>> GetForAdminAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> admin =
            await store.GetAdminDefaultsAsync(cancellationToken);
        return Views(admin, new Dictionary<string, string>(StringComparer.Ordinal));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetEffectiveColorsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (effectiveCache.TryGetValue(userId, out IReadOnlyDictionary<string, string>? cached))
        {
            return cached;
        }

        IReadOnlyDictionary<string, string> admin =
            await store.GetAdminDefaultsAsync(cancellationToken);
        IReadOnlyDictionary<string, string> user =
            await store.GetUserOverridesAsync(userId, cancellationToken);

        Dictionary<string, string> effective = DepartmentCatalog.Departments.ToDictionary(
            item => item.Key,
            item => user.GetValueOrDefault(item.Key)
                ?? admin.GetValueOrDefault(item.Key)
                ?? DepartmentCatalog.DefaultColor(item.Key),
            StringComparer.Ordinal);
        foreach (CalendarPresentationColorDefinition category in CalendarPresentationColorCatalog.Categories)
        {
            effective[category.Key] = user.GetValueOrDefault(category.Key)
                ?? admin.GetValueOrDefault(category.Key)
                ?? category.SystemDefaultColor;
        }

        effectiveCache[userId] = effective;
        return effective;
    }

    public async Task<bool> SetUserColorAsync(
        Guid userId,
        string departmentKey,
        string? color,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ValidateColorKey(departmentKey);
        return await store.SetUserOverrideAsync(
            userId,
            departmentKey,
            NormalizeColor(color),
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public async Task<bool> SetAdminColorAsync(
        string departmentKey,
        string? color,
        string actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ValidateColorKey(departmentKey);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
        {
            throw new ArgumentException(
                "A reason containing at most 1000 characters is required.",
                nameof(reason));
        }

        return await store.SetAdminDefaultAsync(
            departmentKey,
            NormalizeColor(color),
            actor,
            reason.Trim(),
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static IReadOnlyList<DepartmentColorView> Views(
        IReadOnlyDictionary<string, string> admin,
        IReadOnlyDictionary<string, string> user)
    {
        IEnumerable<DepartmentColorView> categories =
            CalendarPresentationColorCatalog.Categories.Select(item =>
            {
                string? adminColor = admin.GetValueOrDefault(item.Key);
                string? userColor = user.GetValueOrDefault(item.Key);
                return new DepartmentColorView
                {
                    Key = item.Key,
                    Name = item.Name,
                    Kind = CalendarColorKind.EventCategory,
                    Description = item.Description,
                    SystemDefaultColor = item.SystemDefaultColor,
                    AdminDefaultColor = adminColor,
                    UserColor = userColor,
                    EffectiveColor = userColor ?? adminColor ?? item.SystemDefaultColor,
                };
            });
        IEnumerable<DepartmentColorView> departments = DepartmentCatalog.Departments.Select(item =>
        {
            string systemColor = DepartmentCatalog.DefaultColor(item.Key);
            string? adminColor = admin.GetValueOrDefault(item.Key);
            string? userColor = user.GetValueOrDefault(item.Key);
            return new DepartmentColorView
            {
                Key = item.Key,
                Name = item.Name,
                Kind = CalendarColorKind.Department,
                Division = item.Division,
                SystemDefaultColor = systemColor,
                AdminDefaultColor = adminColor,
                UserColor = userColor,
                EffectiveColor = userColor ?? adminColor ?? systemColor,
            };
        });
        return categories.Concat(departments).ToArray();
    }

    private static void ValidateColorKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || (!DepartmentCatalog.TryGet(key, out _)
                && !CalendarPresentationColorCatalog.TryGet(key, out _)))
        {
            throw new ArgumentException("Unknown calendar color key.", nameof(key));
        }
    }

    private static string? NormalizeColor(string? color)
    {
        if (color is null)
        {
            return null;
        }

        color = color.Trim().ToUpperInvariant();
        if (!ColorPattern().IsMatch(color))
        {
            throw new ArgumentException("Color must use #RRGGBB format.", nameof(color));
        }

        return color;
    }

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}

public sealed record DepartmentColorView
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required CalendarColorKind Kind { get; init; }
    public DepartmentDivision? Division { get; init; }
    public string? Description { get; init; }
    public required string SystemDefaultColor { get; init; }
    public string? AdminDefaultColor { get; init; }
    public string? UserColor { get; init; }
    public required string EffectiveColor { get; init; }
}

public enum CalendarColorKind
{
    EventCategory,
    Department,
}

public sealed record CalendarPresentationColorDefinition(
    string Key,
    string Name,
    string Description,
    string SystemDefaultColor);

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

internal static class DepartmentColorPaletteResolver
{
    public static Task<IReadOnlyDictionary<string, string>> GetAsync(
        DepartmentColorService service,
        Guid userId,
        CancellationToken cancellationToken) =>
        service.GetEffectiveColorsAsync(userId, cancellationToken);
}
