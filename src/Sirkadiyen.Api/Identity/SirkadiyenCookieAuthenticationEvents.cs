using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Sirkadiyen.Application.Identity;

namespace Sirkadiyen.Api.Identity;

/// <summary>
/// Keeps backend-issued session claims aligned with the authoritative user row
/// and makes API authorization failures status codes rather than HTML redirects.
/// </summary>
public sealed class SirkadiyenCookieAuthenticationEvents(IUserStore userStore)
    : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out Guid userId))
        {
            await RejectAsync(context);
            return;
        }

        UserSession? current = await userStore.FindSessionAsync(
            userId,
            context.HttpContext.RequestAborted);

        if (current is null)
        {
            await RejectAsync(context);
            return;
        }

        ClaimsPrincipal refreshed = UserClaimsPrincipalFactory.Create(current);
        if (!HasSameSessionClaims(context.Principal!, refreshed))
        {
            context.ReplacePrincipal(refreshed);
            context.ShouldRenew = true;
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static bool HasSameSessionClaims(
        ClaimsPrincipal left,
        ClaimsPrincipal right) =>
        Same(left, right, ClaimTypes.NameIdentifier)
        && Same(left, right, ClaimTypes.Email)
        && Same(left, right, ClaimTypes.Name)
        && Same(left, right, ClaimTypes.Role);

    private static bool Same(ClaimsPrincipal left, ClaimsPrincipal right, string claimType) =>
        string.Equals(
            left.FindFirstValue(claimType),
            right.FindFirstValue(claimType),
            StringComparison.Ordinal);

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
