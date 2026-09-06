namespace Sirkadiyen.Application.Meals;

/// <summary>
/// Reads and sets a user's cafeteria-menu preference (ADR-150). Thin, but it keeps the clock and the
/// default off the API endpoint: an absent row means "not chosen", which the product treats as off
/// (opt-in), and the write path stamps the change time the delivery pass finds toggled users by.
/// </summary>
public sealed class MealSubscriptionService(IMealSubscriptionStore store, TimeProvider timeProvider)
{
    /// <summary>Whether the user currently receives the menu. Absent means off (opt-in).</summary>
    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken)
    {
        bool? enabled = await store.GetEnabledAsync(userId, cancellationToken);
        return enabled ?? false;
    }

    /// <summary>Sets the preference. Returns whether it changed.</summary>
    public Task<bool> SetAsync(Guid userId, bool isEnabled, CancellationToken cancellationToken) =>
        store.SetAsync(userId, isEnabled, timeProvider.GetUtcNow(), cancellationToken);
}
