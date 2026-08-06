using Microsoft.AspNetCore.HttpOverrides;

namespace Sirkadiyen.Api.Observability;

/// <summary>
/// Configures forwarded-header handling. TLS is terminated at the edge (the Next dev server
/// locally, a reverse proxy in production) and the request reaches Kestrel over HTTP, so
/// <c>Request.IsHttps</c> is false unless we honour X-Forwarded-Proto. Without this the Secure
/// cookies and the antiforgery SSL guard (SecurePolicy.Always) reject every request (ADR-066).
/// The forwarded headers are trusted only from a known proxy: in Development the sole caller is
/// the local same-host proxy, whose loopback peer can present as ::ffff:127.0.0.1 and not match
/// the default known-network entries, so we trust the immediate peer directly. A production
/// deployment MUST instead pin its reverse proxy through KnownProxies/KnownNetworks (ADR-052).
/// </summary>
public static class ForwardedHeadersConfiguration
{
    public static void Configure(ForwardedHeadersOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        if (isDevelopment)
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        }
    }
}
