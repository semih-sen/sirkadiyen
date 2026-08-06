using Sirkadiyen.Api.Observability;

namespace Sirkadiyen.Api.Composition;

/// <summary>
/// Wires the API's HTTP middleware pipeline in the order the request must flow through it.
/// </summary>
internal static class ApiPipelineExtensions
{
    public static WebApplication UseSirkadiyenApiPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Must run before anything that reads the scheme (auth, antiforgery, cookies).
        app.UseForwardedHeaders();

        // Assign a correlation id as early as possible so every log line and audit event for the
        // request can carry it.
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.UseAntiforgery();

        return app;
    }
}
