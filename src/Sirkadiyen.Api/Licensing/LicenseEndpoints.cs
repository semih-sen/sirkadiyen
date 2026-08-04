using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Api.Licensing;

public static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder licenses = builder
            .MapGroup("/api/licenses")
            .RequireAuthorization()
            .WithTags("Licensing");

        licenses.MapPost("/redeem", RedeemAsync)
            .RequireRateLimiting(RateLimitingPolicies.LicenseRedemption)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Redeems a single-use license for the current user.");

        licenses.MapGet("/status", GetStatusAsync)
            .WithSummary("Returns the current user's license activation state.");

        RouteGroupBuilder administration = builder
            .MapGroup("/api/admin/licenses")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Licensing Administration");

        administration.MapPost("/", CreateAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Creates an active single-use license and returns its code once.");
        administration.MapGet("/", ListLicensesAsync)
            .WithSummary("Lists licenses, newest first, with optional status and kind filters.");
        administration.MapGet("/{licenseId:guid}", FindLicenseAsync)
            .WithSummary("Returns one license with its audit trail; never the code hash.");
        administration.MapPost("/{licenseId:guid}/revoke", RevokeAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Revokes a license without deleting calendar data.");

        builder.MapGroup("/api/admin/users")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("User Administration")
            .MapPost("/{userId:guid}/activate", ActivateManuallyAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Activates a user directly without creating a license code.");

        return builder;
    }

    private static async Task<IResult> RedeemAsync(
        RedeemLicenseRequest request,
        ClaimsPrincipal principal,
        LicenseService licenseService,
        OnboardingStateService onboarding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LicenseRedemptionResult result = await licenseService.RedeemAsync(
            request.Code ?? string.Empty,
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            cancellationToken);

        if (result.Outcome is LicenseRedemptionOutcome.Redeemed
            or LicenseRedemptionOutcome.AlreadyRedeemedByCurrentUser)
        {
            OnboardingSnapshot state = await onboarding.GetAsync(
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                cancellationToken);
            return Results.Ok(new RedeemLicenseResponse
            {
                Outcome = result.Outcome,
                LicenseId = result.LicenseId,
                Onboarding = state,
            });
        }

        if (result.Outcome == LicenseRedemptionOutcome.UserAlreadyActivated)
        {
            return Results.Problem(
                title: "Account already activated",
                detail: "This account already has an active license.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Do not disclose whether a submitted code exists, expired, was
        // revoked, or belongs to another user.
        return Results.Problem(
            title: "License redemption failed",
            detail: "The license code is invalid or unavailable.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> GetStatusAsync(
        ClaimsPrincipal principal,
        ILicenseStore licenseStore,
        CancellationToken cancellationToken)
    {
        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        UserLicenseSummary? summary = await licenseStore.GetUserLicenseSummaryAsync(
            userId,
            cancellationToken);

        UserLicenseState state = summary?.Status switch
        {
            LicenseStatus.Redeemed => UserLicenseState.Active,
            LicenseStatus.Revoked => UserLicenseState.Suspended,
            _ => UserLicenseState.None,
        };

        return Results.Ok(new LicenseStatusResponse
        {
            State = state,
            Kind = summary?.Kind,
            ActivatedAtUtc = summary?.RedeemedAtUtc,
            RevokedAtUtc = summary?.RevokedAtUtc,
        });
    }

    private static async Task<IResult> ListLicensesAsync(
        IAdminLicenseReadStore store,
        CancellationToken cancellationToken,
        LicenseStatus? status = null,
        LicenseKind? kind = null,
        int page = 1,
        int pageSize = 50)
    {
        if (pageSize is < 1 or > 200)
        {
            return ValidationProblem("'pageSize' must be between 1 and 200.");
        }

        return Results.Ok(await store.ListAsync(
            new AdminLicenseQuery
            {
                Status = status,
                Kind = kind,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken));
    }

    private static async Task<IResult> FindLicenseAsync(
        Guid licenseId,
        IAdminLicenseReadStore store,
        CancellationToken cancellationToken) =>
        await store.FindAsync(licenseId, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : Results.Problem(
                title: "License not found",
                detail: $"No license with ID '{licenseId}' exists.",
                statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> CreateAsync(
        CreateLicenseRequest request,
        ClaimsPrincipal principal,
        LicenseService licenseService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            CreatedLicense created = await licenseService.CreateAsync(
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                request.ExpiresAtUtc,
                request.Notes,
                cancellationToken);
            return Results.Created(
                $"/api/admin/licenses/{created.LicenseId}",
                created);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> RevokeAsync(
        Guid licenseId,
        RevokeLicenseRequest request,
        ClaimsPrincipal principal,
        LicenseService licenseService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > License.MaximumReasonLength)
        {
            return ValidationProblem(
                $"'reason' is required and must contain at most {License.MaximumReasonLength} characters.");
        }

        LicenseRevocationResult result = await licenseService.RevokeAsync(
            licenseId,
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            request.Reason,
            cancellationToken);

        return result.Outcome switch
        {
            LicenseRevocationOutcome.NotFound => Results.NotFound(),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> ActivateManuallyAsync(
        Guid userId,
        ManualActivationRequest request,
        ClaimsPrincipal principal,
        LicenseService licenseService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > License.MaximumReasonLength)
        {
            return ValidationProblem(
                $"'reason' is required and must contain at most {License.MaximumReasonLength} characters.");
        }

        ManualLicenseActivationResult result = await licenseService.ActivateManuallyAsync(
            userId,
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            request.Reason,
            cancellationToken);

        return result.Outcome switch
        {
            ManualLicenseActivationOutcome.UserNotFound => Results.NotFound(),
            _ => Results.Ok(result),
        };
    }

    private static IResult ValidationProblem(string detail) => Results.Problem(
        title: "Invalid license request",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);
}

public sealed record RedeemLicenseRequest
{
    public required string? Code { get; init; }
}

public sealed record RedeemLicenseResponse
{
    public required LicenseRedemptionOutcome Outcome { get; init; }

    public Guid? LicenseId { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}

/// <summary>
/// The current user's activation state.
/// </summary>
/// <remarks>
/// No expiry or "time remaining" is returned: Sirkadiyen access does not lapse after activation
/// (a license is single-use activation, not a subscription window). <c>ActivatedAtUtc</c> lets the
/// UI show when access began; a suspended account also carries <c>RevokedAtUtc</c>.
/// </remarks>
public sealed record LicenseStatusResponse
{
    public required UserLicenseState State { get; init; }

    public LicenseKind? Kind { get; init; }

    public DateTimeOffset? ActivatedAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }
}

public sealed record CreateLicenseRequest
{
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record RevokeLicenseRequest
{
    public required string Reason { get; init; }
}

public sealed record ManualActivationRequest
{
    public required string Reason { get; init; }
}
