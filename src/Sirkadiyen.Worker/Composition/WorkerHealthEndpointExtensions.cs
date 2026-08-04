using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using Sirkadiyen.Worker.Health;

namespace Sirkadiyen.Worker.Composition;

internal static class WorkerHealthEndpointExtensions
{
    public static void MapWorkerHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health/live", (WorkerHealthState state) =>
        {
            WorkerHealthSnapshot snapshot = state.GetSnapshot();
            return Results.Ok(snapshot with { Status = "healthy" });
        });
        app.MapGet("/health/ready", (WorkerHealthState state) =>
        {
            WorkerHealthSnapshot snapshot = state.GetSnapshot();
            return string.Equals(snapshot.Status, "healthy", StringComparison.Ordinal)
                ? Results.Ok(snapshot)
                : Results.Json(snapshot, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }
}
