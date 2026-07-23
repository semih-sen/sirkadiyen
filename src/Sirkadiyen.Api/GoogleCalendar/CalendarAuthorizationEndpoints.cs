using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Onboarding;

namespace Sirkadiyen.Api.GoogleCalendar;

public static class CalendarAuthorizationEndpoints
{
    /// <summary>
    /// A Google authorization code is short. A generous ceiling still rejects a body
    /// that could only be an attempt to make the server do pointless work.
    /// </summary>
    private const int MaximumAuthorizationCodeLength = 2048;

    public static IEndpointRouteBuilder MapCalendarAuthorizationEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder calendar = builder
            .MapGroup("/api/calendar/authorization")
            .RequireAuthorization()
            .WithTags("Calendar Authorization");

        calendar.MapGet("/options", GetOptions)
            .WithSummary("Returns the OAuth client and scope the consent screen must request.");
        calendar.MapGet("/", GetAsync)
            .WithSummary("Returns the current user's Calendar authorization, if granted.");
        calendar.MapPost("/", AuthorizeAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Exchanges a Google authorization code for a stored Calendar grant.");

        return builder;
    }

    private static IResult GetOptions(CalendarAuthorizationService authorization) =>
        Results.Ok(new CalendarAuthorizationOptionsResponse
        {
            ClientId = authorization.ClientId,
            Scope = authorization.RequiredScope,
        });

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal principal,
        CalendarAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnectionView? connection = await authorization.GetAsync(
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            cancellationToken);

        return connection is null ? Results.NoContent() : Results.Ok(connection);
    }

    private static async Task<IResult> AuthorizeAsync(
        CalendarAuthorizationRequest request,
        ClaimsPrincipal principal,
        CalendarAuthorizationService authorization,
        OnboardingStateService onboarding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = request.AuthorizationCode?.Trim() ?? string.Empty;
        if (code.Length is 0 or > MaximumAuthorizationCodeLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["authorizationCode"] =
                [
                    "'authorizationCode' is required and must be at most "
                    + $"{MaximumAuthorizationCodeLength} characters.",
                ],
            });
        }

        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        CalendarAuthorizationResult result = await authorization.AuthorizeAsync(
            userId,
            code,
            cancellationToken);

        switch (result.Outcome)
        {
            case CalendarAuthorizationOutcome.Authorized:
                OnboardingSnapshot state = await onboarding.GetAsync(userId, cancellationToken);
                return Results.Ok(new CalendarAuthorizationResponse
                {
                    Connection = result.Connection!,
                    Onboarding = state,
                });

            case CalendarAuthorizationOutcome.PrerequisitesNotMet:
                return Results.Problem(
                    title: "Calendar authorization is not available yet",
                    detail: "Activate the account and complete the academic profile first.",
                    statusCode: StatusCodes.Status409Conflict);

            case CalendarAuthorizationOutcome.InsufficientScope:
                return Results.Problem(
                    title: "Calendar permission was not granted",
                    detail: "Grant Sirkadiyen permission to manage its own calendar, "
                        + "then try again.",
                    statusCode: StatusCodes.Status403Forbidden);

            case CalendarAuthorizationOutcome.ExchangeFailed:
            default:
                // The reason Google gave is deliberately not echoed; the caller recovers
                // by starting consent again for a fresh code.
                return Results.Problem(
                    title: "Google authorization failed",
                    detail: "The authorization code could not be exchanged. Try again.",
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

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
