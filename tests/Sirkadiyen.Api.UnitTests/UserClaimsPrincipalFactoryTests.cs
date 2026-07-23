using System.Security.Claims;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;
using Xunit;

namespace Sirkadiyen.Api.UnitTests;

public sealed class UserClaimsPrincipalFactoryTests
{
    [Fact]
    public void CookiePrincipalContainsOnlyBackendOwnedLocalIdentityAndRole()
    {
        Guid userId = Guid.CreateVersion7();
        UserSession session = new()
        {
            UserId = userId,
            Email = "admin@example.com",
            DisplayName = "Admin",
            Role = UserRole.SuperAdmin,
            LastSignedInAtUtc = DateTimeOffset.UtcNow,
        };

        ClaimsPrincipal principal = UserClaimsPrincipalFactory.Create(session);

        Assert.Equal(userId.ToString("N"), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("admin@example.com", principal.FindFirstValue(ClaimTypes.Email));
        Assert.Equal("Admin", principal.FindFirstValue(ClaimTypes.Name));
        Assert.Equal("SuperAdmin", principal.FindFirstValue(ClaimTypes.Role));
        Assert.True(principal.IsInRole("SuperAdmin"));
        Assert.DoesNotContain(
            principal.Claims,
            claim => claim.Type.Contains("token", StringComparison.OrdinalIgnoreCase)
                || claim.Type.Contains("google", StringComparison.OrdinalIgnoreCase));
    }
}
