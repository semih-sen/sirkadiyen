using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// Read-only SuperAdmin views over user accounts: a searchable list and a per-user detail that
/// composes profile, license history, managed-event count, onboarding state, and recent sign-ins.
/// </summary>
public static class AdminUserEndpoints
{
    private const int MaximumPageSize = 200;

    private const int RecentSignInCount = 10;

    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder users = builder
            .MapGroup("/api/admin/users")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("User Administration");

        users.MapGet("/", ListAsync)
            .WithSummary("Lists user accounts, newest first, with optional email and role filters.");

        users.MapGet("/{userId:guid}", FindAsync)
            .WithSummary("Returns one user with profile, licenses, sync count and recent sign-ins.");

        return builder;
    }

    private static async Task<IResult> ListAsync(
        IAdminUserReadStore store,
        CancellationToken cancellationToken,
        string? search = null,
        UserRole? role = null,
        int page = 1,
        int pageSize = 50)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            return Results.Problem(
                title: "Invalid page size",
                detail: $"'pageSize' must be between 1 and {MaximumPageSize}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(await store.ListAsync(
            new AdminUserQuery
            {
                Search = search,
                Role = role,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken));
    }

    private static async Task<IResult> FindAsync(
        Guid userId,
        IAdminUserReadStore store,
        OnboardingStateService onboarding,
        IAuditEventStore auditStore,
        CancellationToken cancellationToken)
    {
        AdminUserDetail? detail = await store.FindAsync(userId, cancellationToken);
        if (detail is null)
        {
            return Results.Problem(
                title: "User not found",
                detail: $"No user with ID '{userId}' exists.",
                statusCode: StatusCodes.Status404NotFound);
        }

        OnboardingSnapshot onboardingState = await onboarding.GetAsync(userId, cancellationToken);
        var signIns = await auditStore.QueryAsync(
            new AuditEventQuery
            {
                Category = AuditEventCategory.SignIn,
                ActorUserId = userId,
                PageSize = RecentSignInCount,
            },
            cancellationToken);

        return Results.Ok(new AdminUserDetailResponse
        {
            User = detail,
            OnboardingState = onboardingState.State,
            RecentSignIns = signIns.Items,
        });
    }
}

public sealed record AdminUserDetailResponse
{
    public required AdminUserDetail User { get; init; }

    public required OnboardingState OnboardingState { get; init; }

    public required IReadOnlyList<AuditEventView> RecentSignIns { get; init; }
}
