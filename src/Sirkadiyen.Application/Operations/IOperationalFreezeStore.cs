namespace Sirkadiyen.Application.Operations;

/// <summary>
/// Reads and changes the authoritative global operational freeze (ADR-034).
/// </summary>
/// <remarks>
/// Every pipeline boundary reads through this port at runtime. Implementations
/// must throw when the authoritative state cannot be read; callers then make no
/// mutation, which is the fail-closed behavior the switch requires.
/// </remarks>
public interface IOperationalFreezeStore
{
    Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Changes the switch and appends its audit entry in the same transaction.
    /// </summary>
    /// <remarks>
    /// This is intentionally not exposed by the current HTTP API. Real operator
    /// authentication must exist before a remote caller can use it.
    /// </remarks>
    Task<OperationalFreezeChangeResult> SetAsync(
        bool isFrozen,
        string changedBy,
        string reason,
        string correlationId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken);
}

public sealed record OperationalFreezeSnapshot
{
    public required bool IsFrozen { get; init; }

    public string? ChangedBy { get; init; }

    public string? Reason { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset? ChangedAtUtc { get; init; }
}

public sealed record OperationalFreezeChangeResult
{
    public required OperationalFreezeChangeOutcome Outcome { get; init; }

    public required OperationalFreezeSnapshot State { get; init; }
}

public enum OperationalFreezeChangeOutcome
{
    Changed,
    AlreadyInRequestedState,
}
