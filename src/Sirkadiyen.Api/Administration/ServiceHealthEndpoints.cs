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
            .WithSummary("Probes the internal worker and parser health endpoints.");
        return builder;
    }
}
