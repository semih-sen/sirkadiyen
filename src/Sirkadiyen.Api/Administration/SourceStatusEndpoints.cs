using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// Read-only SuperAdmin views over the ingestion pipeline's per-source health, backing the source
/// status dashboard. These read existing pipeline evidence; they never poll or parse.
/// </summary>
public static class SourceStatusEndpoints
{
    public static IEndpointRouteBuilder MapSourceStatusEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder sources = builder
            .MapGroup("/api/admin/sources")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Source Administration");

        sources.MapGet("/", ListAsync)
            .WithSummary("Lists every source with poll status and its latest parse run and revision.");

        sources.MapGet("/{sourceId}", FindAsync)
            .WithSummary("Returns one source's status with its parser profile and recent snapshots.");

        return builder;
    }

    private static async Task<IResult> ListAsync(
        ISourceStatusReadStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(cancellationToken));

    private static async Task<IResult> FindAsync(
        string sourceId,
        ISourceStatusReadStore store,
        CancellationToken cancellationToken) =>
        await store.FindAsync(sourceId, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : Results.Problem(
                title: "Source not found",
                detail: $"No source with ID '{sourceId}' exists.",
                statusCode: StatusCodes.Status404NotFound);
}
