using System.Security.Claims;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Meals;

namespace Sirkadiyen.Api.Meals;

public static class MealEndpoints
{
    public static IEndpointRouteBuilder MapMealEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/meals/subscription", GetAsync)
            .RequireAuthorization()
            .WithTags("Meals")
            .WithSummary("Returns whether the cafeteria lunch menu is on the user's calendar.");

        builder.MapPut("/api/meals/subscription", SetAsync)
            .RequireAuthorization()
            .WithTags("Meals")
            .WithSummary("Turns the cafeteria lunch menu on or off for the user's calendar.");

        return builder;
    }

    private static async Task<MealSubscriptionView> GetAsync(
        ClaimsPrincipal principal,
        MealSubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        bool enabled = await subscriptions.IsEnabledAsync(userId, cancellationToken);
        return new MealSubscriptionView { Enabled = enabled };
    }

    private static async Task<MealSubscriptionView> SetAsync(
        SetMealSubscriptionRequest request,
        ClaimsPrincipal principal,
        MealSubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        // The worker converges the calendar on its next pass — enabling backfills the known window,
        // disabling removes the written events (ADR-150). The endpoint only records the choice.
        await subscriptions.SetAsync(userId, request.Enabled, cancellationToken);
        return new MealSubscriptionView { Enabled = request.Enabled };
    }
}
