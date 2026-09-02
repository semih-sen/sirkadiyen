using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// Exposes the host's own CPU, memory and disk usage — now and 1, 5 and 15 minutes ago — to the admin
/// server dashboard. The values are served from an in-process ring buffer of real samples, so the
/// endpoint itself only reads (ADR-124-style read surface, AI_GUIDELINE §19).
/// </summary>
public static class ServerResourcesEndpoints
{
    public static IEndpointRouteBuilder MapServerResourcesEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet(
                "/api/admin/server/resources",
                (IServerResourceMonitor monitor) => Results.Ok(monitor.GetSnapshot()))
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Observability")
            .WithSummary("Returns the host's current and recent CPU, memory and disk usage.");

        return builder;
    }
}
