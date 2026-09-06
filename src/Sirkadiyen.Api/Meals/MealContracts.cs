namespace Sirkadiyen.Api.Meals;

/// <summary>The current user's cafeteria-menu preference (ADR-150).</summary>
public sealed record MealSubscriptionView
{
    /// <summary>Whether the lunch menu is written to the user's calendar.</summary>
    public required bool Enabled { get; init; }
}

/// <summary>A request to turn the cafeteria menu on or off for the current user.</summary>
public sealed record SetMealSubscriptionRequest
{
    public required bool Enabled { get; init; }
}
