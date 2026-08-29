using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Sirkadiyen.Api.Identity;

/// <summary>
/// Configures the per-caller fixed-window rate limits guarding the sign-in, license redemption,
/// and calendar reconcile endpoints.
/// </summary>
public static class RateLimiterConfiguration
{
    public static void Configure(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(
            RateLimitingPolicies.GoogleSignIn,
            context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                static _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        options.AddPolicy(
            RateLimitingPolicies.LicenseRedemption,
            context => RateLimitPartition.GetFixedWindowLimiter(
                $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous"}:"
                    + $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                static _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        options.AddPolicy(
            RateLimitingPolicies.CalendarReconcile,
            context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                static _ => new FixedWindowRateLimiterOptions
                {
                    // A repair is a heavy, worker-scheduled operation; a few requests an hour is
                    // plenty and stops a user from forcing repeated inventory passes.
                    PermitLimit = 3,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        options.AddPolicy(
            RateLimitingPolicies.RosterLookup,
            context => RateLimitPartition.GetFixedWindowLimiter(
                $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous"}:"
                    + $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                static _ => new FixedWindowRateLimiterOptions
                {
                    // The lookup answers a ten-digit number with a student's name, so
                    // unbounded it would enumerate the faculty one guess at a time. A
                    // student needs the endpoint once, and a few times if they mistype
                    // (AI_GUIDELINE §15).
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    }
}
