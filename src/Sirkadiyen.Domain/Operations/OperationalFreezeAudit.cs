namespace Sirkadiyen.Domain.Operations;

/// <summary>One append-only change to the global operational freeze.</summary>
public sealed class OperationalFreezeAudit
{
    private OperationalFreezeAudit()
    {
        // Materialization constructor.
    }

    internal static OperationalFreezeAudit Create(
        int operationalFreezeControlId,
        bool isFrozen,
        string changedBy,
        string reason,
        string correlationId,
        DateTimeOffset changedAtUtc) => new()
        {
            Id = Guid.CreateVersion7(),
            OperationalFreezeControlId = operationalFreezeControlId,
            IsFrozen = isFrozen,
            ChangedBy = changedBy,
            Reason = reason,
            CorrelationId = correlationId,
            ChangedAtUtc = changedAtUtc,
        };

    public Guid Id { get; private init; }

    public int OperationalFreezeControlId { get; private init; }

    public bool IsFrozen { get; private init; }

    public string ChangedBy { get; private init; } = string.Empty;

    public string Reason { get; private init; } = string.Empty;

    public string CorrelationId { get; private init; } = string.Empty;

    public DateTimeOffset ChangedAtUtc { get; private init; }
}
