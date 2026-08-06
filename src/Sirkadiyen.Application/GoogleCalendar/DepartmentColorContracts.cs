namespace Sirkadiyen.Application.GoogleCalendar;

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
