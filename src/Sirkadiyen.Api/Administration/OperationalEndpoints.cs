using Sirkadiyen.Application.Operations;

namespace Sirkadiyen.Api.Administration;

/// <summary>Read-only operational state until authenticated administration exists.</summary>
public static class OperationalEndpoints
{
    public static IEndpointRouteBuilder MapOperationalEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder operations = builder
            .MapGroup("/api/operations")
            .AddEndpointFilter<AdminApiKeyFilter>()
            .WithTags("Operations");

        operations.MapGet("/freeze", GetFreezeAsync)
            .WithSummary("Returns the runtime global operational freeze state.");

        return builder;
    }

    private static Task<OperationalFreezeSnapshot> GetFreezeAsync(
        IOperationalFreezeStore store,
        CancellationToken cancellationToken) => store.GetAsync(cancellationToken);
}
