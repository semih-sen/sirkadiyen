using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.Identity;

public static class AuthenticationEndpoints
{
    private const int MaximumCredentialLength = 16_384;

    public static IEndpointRouteBuilder MapAuthenticationEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder authentication = builder
            .MapGroup("/api/auth")
            .WithTags("Authentication");

        authentication.MapGet("/csrf", GetCsrfToken)
            .AllowAnonymous()
            .WithSummary("Issues the CSRF token required by state-changing browser requests.");

        authentication.MapPost("/google", SignInWithGoogleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingPolicies.GoogleSignIn)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Verifies a Google ID credential and starts a local cookie session.");

        authentication.MapGet("/me", GetMe)
            .RequireAuthorization()
            .WithSummary("Returns the current local session.");

        authentication.MapPost(
                "/logout",
                (Func<HttpContext, Task<IResult>>)SignOutAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Ends the current local session.");

        return builder;
    }

    private static CsrfTokenResponse GetCsrfToken(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        return new CsrfTokenResponse
        {
            HeaderName = tokens.HeaderName ?? AuthenticationConfiguration.AntiforgeryHeaderName,
            RequestToken = tokens.RequestToken
                ?? throw new InvalidOperationException("No antiforgery request token was issued."),
        };
    }

    private static async Task<IResult> SignInWithGoogleAsync(
        GoogleSignInRequest request,
        GoogleSignInService signIn,
        OnboardingStateService onboarding,
        AuditEventRecorder audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string credential = request.Credential?.Trim() ?? string.Empty;
        if (credential.Length is 0 or > MaximumCredentialLength)
        {
            return Results.Problem(
                title: "Invalid Google credential",
                detail: $"'credential' must contain at most {MaximumCredentialLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        UserSession session;
        try
        {
            session = await signIn.SignInAsync(credential, cancellationToken);
        }
        catch (InvalidGoogleCredentialException)
        {
            // Do not echo validation details or the credential. The caller can
            // safely retry by obtaining a fresh credential from Google.
            return Results.Problem(
                title: "Google sign-in failed",
                detail: "The Google credential is invalid, expired, or has no verified email.",
                statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (GoogleIdentityConflictException)
        {
            return Results.Problem(
                title: "Google identity conflict",
                detail: "This verified email is already linked to another Google identity.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Record the sign-in before issuing the session so a successful authentication always
        // leaves an access-log entry. The IP is masked and encrypted inside the recorder; no raw
        // address is written here (AI_GUIDELINE §15).
        await audit.RecordAsync(
            new AuditEventDraft
            {
                Category = AuditEventCategory.SignIn,
                ActorUserId = session.UserId,
                ActorEmail = session.Email,
                CorrelationId = context.CorrelationId(),
                ClientIp = context.ClientIp(),
                UserAgent = context.ClientUserAgent(),
            },
            cancellationToken);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            UserClaimsPrincipalFactory.Create(session),
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = true,
            });

        OnboardingSnapshot onboardingState = await onboarding.GetAsync(
            session.UserId,
            cancellationToken);
        return Results.Ok(Response(session, onboardingState));
    }

    private static async Task<CurrentUserResponse> GetMe(
        ClaimsPrincipal principal,
        OnboardingStateService onboarding,
        CancellationToken cancellationToken)
    {
        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        OnboardingSnapshot onboardingState = await onboarding.GetAsync(
            userId,
            cancellationToken);

        return new CurrentUserResponse
        {
            UserId = userId,
            Email = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            DisplayName = principal.FindFirstValue(ClaimTypes.Name),
            Role = principal.FindFirstValue(ClaimTypes.Role)
                ?? throw new InvalidOperationException("The session has no role."),
            OnboardingState = onboardingState.State,
        };
    }

    private static async Task<IResult> SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static CurrentUserResponse Response(
        UserSession session,
        OnboardingSnapshot onboarding) => new()
        {
            UserId = session.UserId,
            Email = session.Email,
            DisplayName = session.DisplayName,
            Role = session.Role.ToString(),
            OnboardingState = onboarding.State,
        };
}
