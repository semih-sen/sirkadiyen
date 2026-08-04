using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;

namespace Sirkadiyen.Api.Administration;

public static class ServiceHealthEndpoints
{
    public static IEndpointRouteBuilder MapServiceHealthEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.MapGet(
                "/api/admin/services/health",
                (IAdminServiceHealthProbe probe, CancellationToken cancellationToken) =>
                    probe.GetAsync(cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Observability")
            .WithSummary("Returns the worker heartbeat and a point-in-time parser health probe.");
        return builder;
    }
}
