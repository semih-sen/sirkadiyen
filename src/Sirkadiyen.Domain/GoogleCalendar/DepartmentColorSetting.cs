namespace Sirkadiyen.Domain.GoogleCalendar;

public sealed class DepartmentColorSetting
{
    public const int MaximumDepartmentKeyLength = 100;
    public const int ColorLength = 7;

    private DepartmentColorSetting()
    {
        DepartmentKey = string.Empty;
        BackgroundColor = string.Empty;
        UpdatedBy = string.Empty;
    }

    public string DepartmentKey { get; private init; }
    public string BackgroundColor { get; private set; }
    public string UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public uint RowVersion { get; private set; }

    public static DepartmentColorSetting Create(
        string departmentKey,
        string backgroundColor,
        string updatedBy,
        DateTimeOffset atUtc) =>
        new()
        {
            DepartmentKey = Required(departmentKey, MaximumDepartmentKeyLength),
            BackgroundColor = Color(backgroundColor),
            UpdatedBy = Required(updatedBy, 320),
            UpdatedAtUtc = atUtc,
        };

    public void Change(string backgroundColor, string updatedBy, DateTimeOffset atUtc)
    {
        BackgroundColor = Color(backgroundColor);
        UpdatedBy = Required(updatedBy, 320);
        UpdatedAtUtc = atUtc;
    }

    internal static string Color(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim().ToUpperInvariant();
        if (value.Length != ColorLength
            || value[0] != '#'
            || value[1..].Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Color must use #RRGGBB format.", nameof(value));
        }

        return value;
    }

    internal static string Required(string value, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength);
        return value;
    }
}
