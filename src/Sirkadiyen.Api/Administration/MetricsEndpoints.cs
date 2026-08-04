using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// A SuperAdmin JSON snapshot of operational counts, backing the admin server dashboard. It is a
/// read aggregation over existing tables — not a metrics-stack exporter, which would be a separate,
/// recorded decision.
/// </summary>
public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/admin/metrics", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Operations")
            .WithSummary("Returns a point-in-time snapshot of operational counts.");

        return builder;
    }

    private static async Task<IResult> GetAsync(
        IAdminMetricsReadStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.GetAsync(cancellationToken));
}
