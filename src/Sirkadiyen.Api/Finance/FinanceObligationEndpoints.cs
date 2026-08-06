using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// SuperAdmin-only administration of the accrual layer beside the cash ledger: receivables and
/// debts, their settlements, write-offs and cancellations (ADR-093).
/// </summary>
public static class FinanceObligationEndpoints
{
    private const int MaximumPageSize = 200;

    public static IEndpointRouteBuilder MapFinanceObligationEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder obligations = builder
            .MapGroup("/api/admin/finance/obligations")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Finance Obligations");

        obligations.MapGet("/", ListAsync)
            .WithSummary("Lists receivables and debts, newest issue date first.");
        obligations.MapGet("/{obligationId:guid}", FindAsync)
            .WithSummary("Returns one obligation.");
        obligations.MapPost("/", CreateAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Creates a receivable or a debt.");
        obligations.MapPost("/{obligationId:guid}/settle", SettleAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Records a cash transaction that settles part or all of an obligation.");
        obligations.MapPost("/{obligationId:guid}/settlements/{settlementId:guid}/cancel", CancelSettlementAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Un-links a settlement without reversing the cash transaction it produced.");
        obligations.MapPost("/{obligationId:guid}/write-off", WriteOffAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Writes off the remaining amount as uncollectible or unpaid.");
        obligations.MapPost("/{obligationId:guid}/cancel", CancelAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Cancels an obligation that was never settled.");

        return builder;
    }

    private static async Task<IResult> ListAsync(
        IFinanceObligationStore store,
        CancellationToken cancellationToken,
        FinanceObligationDirection? direction = null,
        FinanceObligationStatus? status = null,
        int page = 1,
        int pageSize = 50)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            return InvalidPageSize();
        }

        PagedResult<FinanceObligationListItem> result = await store.ListAsync(
            new FinanceObligationQuery
            {
                Page = page,
                PageSize = pageSize,
                Direction = direction,
                Status = status,
            },
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> FindAsync(
        Guid obligationId,
        IFinanceObligationStore store,
        CancellationToken cancellationToken) =>
        await store.FindAsync(obligationId, cancellationToken) is { } obligation
            ? Results.Ok(obligation)
            : ObligationNotFound(obligationId);

    private static async Task<IResult> CreateAsync(
        CreateFinanceObligationRequest request,
        ClaimsPrincipal principal,
        FinanceObligationService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            FinanceObligationMutationResult result = await service.CreateAsync(
                request.Direction,
                request.Category,
                request.CounterpartyName ?? string.Empty,
                request.Description,
                request.Amount,
                request.IssuedOn,
                request.DueOn,
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                cancellationToken);
            return result.Outcome == FinanceObligationOutcome.Created
                ? Results.Created($"/api/admin/finance/obligations/{result.ObligationId}", result)
                : MapOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> SettleAsync(
        Guid obligationId,
        SettleFinanceObligationRequest request,
        ClaimsPrincipal principal,
        FinanceObligationService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            FinanceObligationMutationResult result = await service.SettleAsync(
                obligationId,
                request.AccountId,
                request.Amount,
                request.SettledOn,
                request.Reference,
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                context.CorrelationId(),
                cancellationToken);
            return MapOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> CancelSettlementAsync(
        Guid obligationId,
        Guid settlementId,
        CancelFinanceObligationSettlementRequest request,
        ClaimsPrincipal principal,
        FinanceObligationService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ValidationProblem("A reason is required to cancel a settlement.");
        }

        FinanceObligationMutationResult result = await service.CancelSettlementAsync(
            obligationId,
            settlementId,
            request.Reason,
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            context.CorrelationId(),
            cancellationToken);
        return MapOutcome(result);
    }

    private static async Task<IResult> WriteOffAsync(
        Guid obligationId,
        CloseFinanceObligationRequest request,
        ClaimsPrincipal principal,
        FinanceObligationService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ValidationProblem("A reason is required to write off an obligation.");
        }

        FinanceObligationMutationResult result = await service.WriteOffAsync(
            obligationId,
            request.Reason,
            request.On,
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            context.CorrelationId(),
            cancellationToken);
        return MapOutcome(result);
    }

    private static async Task<IResult> CancelAsync(
        Guid obligationId,
        CloseFinanceObligationRequest request,
        ClaimsPrincipal principal,
        FinanceObligationService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ValidationProblem("A reason is required to cancel an obligation.");
        }

        FinanceObligationMutationResult result = await service.CancelAsync(
            obligationId,
            request.Reason,
            request.On,
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            context.CorrelationId(),
            cancellationToken);
        return MapOutcome(result);
    }

    private static IResult MapOutcome(FinanceObligationMutationResult result) => result.Outcome switch
    {
        FinanceObligationOutcome.Created or FinanceObligationOutcome.Settled
            or FinanceObligationOutcome.SettlementCancelled or FinanceObligationOutcome.WrittenOff
            or FinanceObligationOutcome.Cancelled => Results.Ok(result),
        FinanceObligationOutcome.NotFound => ObligationNotFound(result.ObligationId ?? Guid.Empty),
        FinanceObligationOutcome.SettlementNotFound => Results.Problem(
            title: "Settlement not found",
            statusCode: StatusCodes.Status404NotFound),
        FinanceObligationOutcome.AccountNotFound => Results.Problem(
            title: "Account not found",
            statusCode: StatusCodes.Status404NotFound),
        FinanceObligationOutcome.AccountClosed => Results.Problem(
            title: "Account is closed",
            statusCode: StatusCodes.Status409Conflict),
        FinanceObligationOutcome.AlreadyClosed => Results.Problem(
            title: "Obligation already closed",
            statusCode: StatusCodes.Status409Conflict),
        FinanceObligationOutcome.OverSettlement => Results.Problem(
            title: "Settlement exceeds the remaining amount",
            statusCode: StatusCodes.Status409Conflict),
        FinanceObligationOutcome.NothingSettledToCancel => Results.Problem(
            title: "Nothing settled to cancel",
            statusCode: StatusCodes.Status409Conflict),
        FinanceObligationOutcome.ConcurrentUpdate => Results.Problem(
            title: "Concurrent update",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
    };

    private static IResult ObligationNotFound(Guid obligationId) => Results.Problem(
        title: "Obligation not found",
        detail: $"No obligation with ID '{obligationId}' exists.",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult InvalidPageSize() => Results.Problem(
        title: "Invalid page size",
        detail: $"'pageSize' must be between 1 and {MaximumPageSize}.",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult ValidationProblem(string detail) => Results.Problem(
        title: "Invalid request",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);
}
