namespace Sirkadiyen.Domain.GoogleCalendar;

public sealed class UserDepartmentColorPreference
{
    private UserDepartmentColorPreference()
    {
        DepartmentKey = string.Empty;
        BackgroundColor = string.Empty;
    }

    public Guid UserId { get; private init; }
    public string DepartmentKey { get; private init; }
    public string BackgroundColor { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static UserDepartmentColorPreference Create(
        Guid userId,
        string departmentKey,
        string backgroundColor,
        DateTimeOffset atUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A preference owner is required.", nameof(userId));
        }

        return new()
        {
            UserId = userId,
            DepartmentKey = DepartmentColorSetting.Required(
                departmentKey,
                DepartmentColorSetting.MaximumDepartmentKeyLength),
            BackgroundColor = DepartmentColorSetting.Color(backgroundColor),
            UpdatedAtUtc = atUtc,
        };
    }

    public void Change(string backgroundColor, DateTimeOffset atUtc)
    {
        BackgroundColor = DepartmentColorSetting.Color(backgroundColor);
        UpdatedAtUtc = atUtc;
    }
}
