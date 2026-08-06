namespace Sirkadiyen.Domain.Operations;

public sealed class ScopedOperationalFreezeAudit
{
    private ScopedOperationalFreezeAudit() { }

    internal static ScopedOperationalFreezeAudit Create(
        Guid controlId,
        bool isFrozen,
        string changedBy,
        string reason,
        string correlationId,
        DateTimeOffset changedAtUtc) => new()
        {
            Id = Guid.CreateVersion7(),
            ScopedOperationalFreezeControlId = controlId,
            IsFrozen = isFrozen,
            ChangedBy = changedBy,
            Reason = reason,
            CorrelationId = correlationId,
            ChangedAtUtc = changedAtUtc,
        };

    public Guid Id { get; private init; }
    public Guid ScopedOperationalFreezeControlId { get; private init; }
    public bool IsFrozen { get; private init; }
    public string ChangedBy { get; private init; } = string.Empty;
    public string Reason { get; private init; } = string.Empty;
    public string CorrelationId { get; private init; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; private init; }
}
