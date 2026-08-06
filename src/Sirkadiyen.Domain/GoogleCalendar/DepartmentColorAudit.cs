namespace Sirkadiyen.Domain.GoogleCalendar;

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
