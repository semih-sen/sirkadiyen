using System.Security.Claims;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Onboarding;

namespace Sirkadiyen.Api.Onboarding;

public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/onboarding", GetAsync)
            .RequireAuthorization()
            .WithTags("Onboarding")
            .WithSummary("Returns resumable onboarding state derived from backend records.");

        return builder;
    }

    private static Task<OnboardingSnapshot> GetAsync(
        ClaimsPrincipal principal,
        OnboardingStateService onboarding,
        CancellationToken cancellationToken) => onboarding.GetAsync(
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            cancellationToken);
}
