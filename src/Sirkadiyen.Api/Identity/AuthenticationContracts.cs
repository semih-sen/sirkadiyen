using Sirkadiyen.Application.Onboarding;

namespace Sirkadiyen.Api.Identity;

public sealed record GoogleSignInRequest
{
    public required string? Credential { get; init; }
}

public sealed record CsrfTokenResponse
{
    public required string HeaderName { get; init; }

    public required string RequestToken { get; init; }
}

public sealed record CurrentUserResponse
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    public required string Role { get; init; }

    public required OnboardingState OnboardingState { get; init; }
}
