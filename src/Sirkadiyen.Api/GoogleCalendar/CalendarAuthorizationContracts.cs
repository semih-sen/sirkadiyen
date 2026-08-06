using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Onboarding;

namespace Sirkadiyen.Api.GoogleCalendar;

public sealed record CalendarAuthorizationRequest
{
    public string? AuthorizationCode { get; init; }
}

public sealed record CalendarAuthorizationResponse
{
    public required GoogleCalendarConnectionView Connection { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}

/// <summary>What the frontend needs to start the Google consent screen.</summary>
public sealed record CalendarAuthorizationOptionsResponse
{
    public required string ClientId { get; init; }

    public required string Scope { get; init; }
}
