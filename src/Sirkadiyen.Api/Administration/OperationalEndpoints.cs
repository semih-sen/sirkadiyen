using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Operations;
using Sirkadiyen.Domain.ScheduleSources;

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

        operations.MapGet("/freeze/scopes", ListScopedFreezesAsync)
            .WithSummary("Lists class-year/program-language operational freeze controls.");
        operations.MapPost("/freeze/scopes", SetScopedFreezeAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Freezes or unfreezes one class-year/program-language pipeline.");

        return builder;
    }

    private static Task<OperationalFreezeSnapshot> GetFreezeAsync(
        IOperationalFreezeStore store,
        CancellationToken cancellationToken) => store.GetAsync(cancellationToken);

    private static Task<IReadOnlyList<OperationalFreezeSnapshot>> ListScopedFreezesAsync(
        IOperationalFreezeStore store,
        CancellationToken cancellationToken) => store.ListScopedAsync(cancellationToken);

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

    private static async Task<IResult> SetScopedFreezeAsync(
        SetScopedOperationalFreezeRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOperationalFreezeStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ClassYear is < 1 or > 6 || !Enum.IsDefined(request.ProgramLanguage))
        {
            return Results.Problem(
                title: "Invalid operational freeze scope",
                detail: "'classYear' must be between 1 and 6 and 'programLanguage' must be supported.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > OperationalFreezeControl.MaximumReasonLength)
        {
            return Results.Problem(
                title: "Invalid operational freeze request",
                detail: $"'reason' is required and must contain at most {OperationalFreezeControl.MaximumReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string correlationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        OperationalFreezeChangeResult result = await store.SetScopedAsync(
            new OperationalFreezeScope
            {
                ClassYear = request.ClassYear,
                ProgramLanguage = request.ProgramLanguage,
            },
            request.IsFrozen,
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            request.Reason,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return Results.Ok(result);
    }
}
