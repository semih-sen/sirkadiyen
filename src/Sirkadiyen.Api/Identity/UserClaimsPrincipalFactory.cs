using System.Security.Claims;
using Sirkadiyen.Application.Identity;

namespace Sirkadiyen.Api.Identity;

public static class UserClaimsPrincipalFactory
{
    public static ClaimsPrincipal Create(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, session.UserId.ToString("N")),
            new(ClaimTypes.Email, session.Email),
            new(ClaimTypes.Name, session.DisplayName ?? session.Email),
            new(ClaimTypes.Role, session.Role.ToString()),
        ];

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, "SirkadiyenCookie"));
    }

    public static Guid GetRequiredUserId(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)
            ? userId
            : throw new InvalidOperationException(
                "The authenticated principal has no valid local user ID.");
    }

    public static string GetRequiredEmail(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue(ClaimTypes.Email) is { Length: > 0 } email
            ? email
            : throw new InvalidOperationException(
                "The authenticated principal has no verified email.");
    }
}
