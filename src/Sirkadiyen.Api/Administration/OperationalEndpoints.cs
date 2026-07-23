using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Operations;

namespace Sirkadiyen.Api.Administration;

/// <summary>Administrative operational state.</summary>
public static class OperationalEndpoints
{
    public static IEndpointRouteBuilder MapOperationalEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder operations = builder
            .MapGroup("/api/operations")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Operations");

        operations.MapGet("/freeze", GetFreezeAsync)
            .WithSummary("Returns the runtime global operational freeze state.");
        operations.MapPost("/freeze", SetFreezeAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Freezes or unfreezes mutating pipelines with an audit entry.");

        return builder;
    }

    private static Task<OperationalFreezeSnapshot> GetFreezeAsync(
        IOperationalFreezeStore store,
        CancellationToken cancellationToken) => store.GetAsync(cancellationToken);

    private static async Task<IResult> SetFreezeAsync(
        SetOperationalFreezeRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOperationalFreezeStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > OperationalFreezeControl.MaximumReasonLength)
        {
            return Results.Problem(
                title: "Invalid operational freeze request",
                detail: $"'reason' is required and must contain at most "
                    + $"{OperationalFreezeControl.MaximumReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string correlationId = Activity.Current?.TraceId.ToString()
            ?? context.TraceIdentifier;
        OperationalFreezeChangeResult result = await store.SetAsync(
            request.IsFrozen,
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            request.Reason,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return Results.Ok(result);
    }
}

public sealed record SetOperationalFreezeRequest
{
    public required bool IsFrozen { get; init; }

    public required string Reason { get; init; }
}
