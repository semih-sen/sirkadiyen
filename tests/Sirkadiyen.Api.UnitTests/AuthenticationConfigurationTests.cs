using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Sirkadiyen.Api.Identity;
using Xunit;

namespace Sirkadiyen.Api.UnitTests;

public sealed class AuthenticationConfigurationTests
{
    [Fact]
    public void BrowserSessionUsesExplicitSecureCookiePolicy()
    {
        CookieAuthenticationOptions options = new();

        AuthenticationConfiguration.ConfigureCookie(options);

        Assert.Equal("__Host-Sirkadiyen.Session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Equal(TimeSpan.FromDays(30), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.Equal(
            typeof(SirkadiyenCookieAuthenticationEvents),
            options.EventsType);
    }
}
