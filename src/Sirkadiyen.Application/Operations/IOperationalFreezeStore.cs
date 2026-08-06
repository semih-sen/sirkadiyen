using Sirkadiyen.Domain.Scheduling.Sources;

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

    /// <summary>Returns the explicit switch for one class/program pipeline.</summary>
    Task<OperationalFreezeSnapshot> GetScopedAsync(
        OperationalFreezeScope scope,
        CancellationToken cancellationToken) =>
        Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = false, Scope = scope });

    Task<IReadOnlyList<OperationalFreezeSnapshot>> ListScopedAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OperationalFreezeSnapshot>>([]);

    Task<OperationalFreezeChangeResult> SetScopedAsync(
        OperationalFreezeScope scope,
        bool isFrozen,
        string changedBy,
        string reason,
        string correlationId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This operational freeze store does not support scoped controls.");

    /// <summary>Combines the global emergency stop with the selected pipeline switch.</summary>
    async Task<bool> IsFrozenAsync(
        OperationalFreezeScope scope,
        CancellationToken cancellationToken)
    {
        if ((await GetAsync(cancellationToken)).IsFrozen)
        {
            return true;
        }

        return (await GetScopedAsync(scope, cancellationToken)).IsFrozen;
    }
}

public sealed record OperationalFreezeScope
{
    public required int ClassYear { get; init; }
    public required ProgramLanguage ProgramLanguage { get; init; }
}

public sealed record OperationalFreezeSnapshot
{
    public required bool IsFrozen { get; init; }

    /// <summary>Null for the global emergency stop.</summary>
    public OperationalFreezeScope? Scope { get; init; }

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
