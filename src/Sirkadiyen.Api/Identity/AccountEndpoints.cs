using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Sirkadiyen.Application.Identity;

namespace Sirkadiyen.Api.Identity;

/// <summary>
/// The account owner's self-service surface. Today that is exactly one action: permanently deleting
/// their own account ("Hesabımı sil", ADR-118).
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder account = builder
            .MapGroup("/api/account")
            .RequireAuthorization()
            .WithTags("Account");

        account.MapPost("/delete", DeleteOwnAccountAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Permanently deletes the caller's own account.");

        return builder;
    }

    private static async Task<IResult> DeleteOwnAccountAsync(
        DeleteOwnAccountRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        AccountDeletionService deletion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        string email = UserClaimsPrincipalFactory.GetRequiredEmail(principal);

        AccountDeletionResult result = await deletion.DeleteAsync(
            new AccountDeletionRequest
            {
                UserId = userId,
                RequestedByOperator = false,
                ActorUserId = userId,
                ActorEmail = email,
                ConfirmEmail = request.ConfirmEmail,
                Reason = null,
                CorrelationId = context.CorrelationId(),
                ClientIp = context.ClientIp(),
                UserAgent = context.ClientUserAgent(),
            },
            cancellationToken);

        switch (result.Outcome)
        {
            case AccountDeletionOutcome.Deleted:
                // End the session the deleted account was signed in with; its cookie must not keep
                // authorizing requests for a user that no longer exists.
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Ok(AccountDeletionResponse.From(result));

            case AccountDeletionOutcome.EmailMismatch:
                return Results.Problem(
                    title: "Confirmation does not match",
                    detail: "Type the e-mail address of this account exactly to confirm deletion.",
                    statusCode: StatusCodes.Status400BadRequest);

            case AccountDeletionOutcome.SuperAdminRefused:
                return Results.Problem(
                    title: "Administrator account cannot be deleted",
                    detail: "An administrator account cannot be deleted from the self-service flow.",
                    statusCode: StatusCodes.Status409Conflict);

            default:
                // The caller is authenticated, so their user existed a moment ago; a concurrent
                // deletion is the only way here. Their session is already invalid — sign them out.
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Problem(
                    title: "Account not found",
                    detail: "This account no longer exists.",
                    statusCode: StatusCodes.Status404NotFound);
        }
    }
}

/// <summary>The self-service deletion request body: the confirmation phrase only.</summary>
public sealed record DeleteOwnAccountRequest
{
    /// <summary>The caller's own e-mail, retyped to confirm the irreversible deletion.</summary>
    public string? ConfirmEmail { get; init; }
}

/// <summary>
/// What the deletion did to the external Google footprint, so the UI can tell the user whether
/// their Sirkadiyen calendar was removed or whether they should remove it themselves.
/// </summary>
public sealed record AccountDeletionResponse
{
    public required bool HadManagedCalendar { get; init; }

    public required bool GoogleCalendarDeleted { get; init; }

    public required bool GoogleTokenRevoked { get; init; }

    public static AccountDeletionResponse From(AccountDeletionResult result) => new()
    {
        HadManagedCalendar = result.HadManagedCalendar,
        GoogleCalendarDeleted = result.GoogleCalendarDeleted,
        GoogleTokenRevoked = result.GoogleTokenRevoked,
    };
}
