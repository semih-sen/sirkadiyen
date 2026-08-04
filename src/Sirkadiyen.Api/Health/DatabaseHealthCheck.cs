using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sirkadiyen.Infrastructure.Persistence;

namespace Sirkadiyen.Api.Health;

/// <summary>
/// Reports the database as a readiness dependency: the API can serve almost nothing without it, so
/// a host that cannot reach PostgreSQL must not be routed traffic.
/// </summary>
public sealed class DatabaseHealthCheck(SirkadiyenDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database connectivity check failed.", exception);
        }
    }
}
