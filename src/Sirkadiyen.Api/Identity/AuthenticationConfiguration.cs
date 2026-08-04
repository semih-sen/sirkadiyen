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
        // A remembered same-browser session should survive ordinary study breaks without
        // making the Google ID credential itself long-lived. The cookie ticket remains
        // backend-issued, HTTP-only, secure and revalidated against the user row on every
        // request; sliding expiry renews the 30-day window while the browser is in use.
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.EventsType = typeof(SirkadiyenCookieAuthenticationEvents);
    }
}
