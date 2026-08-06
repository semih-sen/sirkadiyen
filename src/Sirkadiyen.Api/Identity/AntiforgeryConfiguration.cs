using Microsoft.AspNetCore.Antiforgery;

namespace Sirkadiyen.Api.Identity;

/// <summary>
/// Configures the antiforgery cookie and header used by the double-submit CSRF protection.
/// </summary>
public static class AntiforgeryConfiguration
{
    public static void Configure(AntiforgeryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.HeaderName = AuthenticationConfiguration.AntiforgeryHeaderName;
        options.Cookie.Name = AuthenticationConfiguration.AntiforgeryCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
    }
}
