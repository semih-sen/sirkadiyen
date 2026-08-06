namespace Sirkadiyen.Application.GoogleCalendar;

internal static class DepartmentColorPaletteResolver
{
    public static Task<IReadOnlyDictionary<string, string>> GetAsync(
        DepartmentColorService service,
        Guid userId,
        CancellationToken cancellationToken) =>
        service.GetEffectiveColorsAsync(userId, cancellationToken);
}
