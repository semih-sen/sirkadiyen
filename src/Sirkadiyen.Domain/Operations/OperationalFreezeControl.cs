namespace Sirkadiyen.Domain.Operations;

/// <summary>
/// The authoritative, runtime-readable switch that stops mutating schedule
/// pipeline boundaries (ADR-034).
/// </summary>
/// <remarks>
/// There is exactly one row. The current values make the hot-path read cheap;
/// every actual transition is also copied to <see cref="OperationalFreezeAudit"/>
/// so changing the switch never rewrites its history.
/// </remarks>
public sealed class OperationalFreezeControl
{
    public const int SingletonId = 1;

    public const int MaximumActorLength = 200;

    public const int MaximumReasonLength = 2000;

    public const int MaximumCorrelationIdLength = 100;

    private OperationalFreezeControl()
    {
        // Materialization constructor.
    }

    public static OperationalFreezeControl CreateInitial() => new()
    {
        Id = SingletonId,
        IsFrozen = false,
    };

    public int Id { get; private init; }

    public bool IsFrozen { get; private set; }

    /// <summary>The reason for the latest freeze or unfreeze transition.</summary>
    public string? Reason { get; private set; }

    /// <summary>The actor responsible for the latest transition.</summary>
    public string? ChangedBy { get; private set; }

    public DateTimeOffset? ChangedAtUtc { get; private set; }

    public string? CorrelationId { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Changes the switch and returns the immutable audit row to append.</summary>
    public OperationalFreezeAudit Change(
        bool isFrozen,
        string changedBy,
        string reason,
        string correlationId,
        DateTimeOffset changedAtUtc)
    {
        changedBy = RequiredBounded(
            changedBy,
            MaximumActorLength,
            nameof(changedBy));
        reason = RequiredBounded(reason, MaximumReasonLength, nameof(reason));
        correlationId = RequiredBounded(
            correlationId,
            MaximumCorrelationIdLength,
            nameof(correlationId));

        if (isFrozen == IsFrozen)
        {
            throw new InvalidOperationException(
                $"The global operational freeze is already {(isFrozen ? "enabled" : "disabled")}.");
        }

        IsFrozen = isFrozen;
        ChangedBy = changedBy;
        Reason = reason;
        CorrelationId = correlationId;
        ChangedAtUtc = changedAtUtc;

        return OperationalFreezeAudit.Create(
            Id,
            isFrozen,
            changedBy,
            reason,
            correlationId,
            changedAtUtc);
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }
}

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
