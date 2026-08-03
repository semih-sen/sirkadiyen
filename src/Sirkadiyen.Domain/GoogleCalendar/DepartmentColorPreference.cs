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

public sealed class DepartmentColorAudit
{
    public const int MaximumActorLength = 320;
    public const int MaximumReasonLength = 1000;
    public const int MaximumCorrelationIdLength = 100;

    private DepartmentColorAudit()
    {
        DepartmentKey = string.Empty;
        Actor = string.Empty;
        CorrelationId = string.Empty;
    }

    public Guid Id { get; private init; }
    public DepartmentColorScope Scope { get; private init; }
    public Guid? UserId { get; private init; }
    public string DepartmentKey { get; private init; }
    public string? PreviousColor { get; private init; }
    public string? NewColor { get; private init; }
    public string Actor { get; private init; }
    public string? Reason { get; private init; }
    public string CorrelationId { get; private init; }
    public DateTimeOffset ChangedAtUtc { get; private init; }

    public static DepartmentColorAudit Create(
        DepartmentColorScope scope,
        Guid? userId,
        string departmentKey,
        string? previousColor,
        string? newColor,
        string actor,
        string? reason,
        string correlationId,
        DateTimeOffset atUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Scope = scope,
            UserId = userId,
            DepartmentKey = DepartmentColorSetting.Required(
                departmentKey,
                DepartmentColorSetting.MaximumDepartmentKeyLength),
            PreviousColor = previousColor,
            NewColor = newColor,
            Actor = DepartmentColorSetting.Required(actor, MaximumActorLength),
            Reason = string.IsNullOrWhiteSpace(reason)
                ? null
                : DepartmentColorSetting.Required(reason, MaximumReasonLength),
            CorrelationId = DepartmentColorSetting.Required(
                correlationId,
                MaximumCorrelationIdLength),
            ChangedAtUtc = atUtc,
        };
}

public enum DepartmentColorScope
{
    AdminDefault,
    UserOverride,
}
