using Microsoft.AspNetCore.Authentication.Cookies;

namespace Sirkadiyen.Api.Identity;

public static class AuthenticationConfiguration
{
    public const string SessionCookieName = "__Host-Sirkadiyen.Session";

    public const string AntiforgeryCookieName = "__Host-Sirkadiyen.Antiforgery";

    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    public static void ConfigureCookie(CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Cookie.Name = SessionCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.EventsType = typeof(SirkadiyenCookieAuthenticationEvents);
    }
}
