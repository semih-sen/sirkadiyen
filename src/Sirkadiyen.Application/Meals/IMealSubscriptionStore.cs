namespace Sirkadiyen.Application.Meals;

/// <summary>Persists each user's standing choice to receive the cafeteria menu (ADR-150).</summary>
public interface IMealSubscriptionStore
{
    /// <summary>The user's choice, or null when they have never made one.</summary>
    Task<bool?> GetEnabledAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the choice, creating the row on first use. Returns whether it actually changed, so
    /// the caller does not re-queue delivery work for a toggle that restated the current state.
    /// </summary>
    Task<bool> SetAsync(
        Guid userId,
        bool isEnabled,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
