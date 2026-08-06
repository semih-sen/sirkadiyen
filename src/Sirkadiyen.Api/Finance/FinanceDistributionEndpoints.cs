using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// The six-step high-risk profit distribution flow: scope, server-side compute, review, preview
/// with a binding hash, strong confirmation, audit (design-plan §4.3, ADR-093).
/// </summary>
public static class FinanceDistributionEndpoints
{
    public static IEndpointRouteBuilder MapFinanceDistributionEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder distributions = builder
            .MapGroup("/api/admin/finance/distributions")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Finance Distributions");

        distributions.MapGet("/", ListAsync)
            .WithSummary("Lists executed and reversed profit distributions.");
        distributions.MapGet("/{distributionId:guid}", FindAsync)
            .WithSummary("Returns one distribution.");
        distributions.MapPost("/preview", PreviewAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Computes the distribution plan server-side. Writes nothing.");
        distributions.MapPost("/execute", ExecuteAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary(
                "Executes a previewed plan. Recomputes and compares the plan hash; a stale " +
                "preview is refused rather than silently re-run.");
        distributions.MapPost("/{distributionId:guid}/reverse", ReverseAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Reverses a distribution with compensating inflow transactions.");

        return builder;
    }

    private static async Task<IResult> ListAsync(
        IFinanceDistributionStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(cancellationToken));

    private static async Task<IResult> FindAsync(
        Guid distributionId,
        IFinanceDistributionStore store,
        CancellationToken cancellationToken) =>
        await store.FindAsync(distributionId, cancellationToken) is { } distribution
            ? Results.Ok(distribution)
            : Results.Problem(
                title: "Distribution not found",
                detail: $"No distribution with ID '{distributionId}' exists.",
                statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> PreviewAsync(
        PreviewFinanceDistributionRequest request,
        FinanceDistributionService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        FinanceDistributionPlan plan = await service.PreviewAsync(
            request.PeriodStartOn,
            request.PeriodEndOn,
            request.SourceAccountId,
            cancellationToken);

        return plan.Outcome switch
        {
            FinanceDistributionPlanOutcome.Ready => Results.Ok(plan),
            FinanceDistributionPlanOutcome.NothingToDistribute => Results.Ok(plan),
            FinanceDistributionPlanOutcome.NoEligiblePartners => Results.Ok(plan),
            FinanceDistributionPlanOutcome.SharesDoNotSumToTotal => Results.Ok(plan),
            FinanceDistributionPlanOutcome.AlreadyDistributedForPeriod => Results.Ok(plan),
            FinanceDistributionPlanOutcome.SourceAccountNotFound => Results.Problem(
                title: "Account not found",
                statusCode: StatusCodes.Status404NotFound),
            FinanceDistributionPlanOutcome.SourceAccountClosed => Results.Problem(
                title: "Account is closed",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> ExecuteAsync(
        ExecuteFinanceDistributionRequest request,
        ClaimsPrincipal principal,
        FinanceDistributionService service,
        AuditEventRecorder audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ValidationProblem("A reason is required to execute a distribution.");
        }

        Guid actorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        string actorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal);

        FinanceDistributionResult result = await service.ExecuteAsync(
            request.PeriodStartOn,
            request.PeriodEndOn,
            request.SourceAccountId,
            request.ConfirmationToken,
            request.PlanHash ?? string.Empty,
            request.ExpectedConfirmationPhrase ?? string.Empty,
            request.Reason,
            actorUserId,
            actorEmail,
            context.CorrelationId(),
            cancellationToken);

        if (result.Outcome == FinanceDistributionOutcome.Executed)
        {
            await audit.RecordAsync(
                new AuditEventDraft
                {
                    Category = AuditEventCategory.FinanceDistributionExecuted,
                    ActorUserId = actorUserId,
                    ActorEmail = actorEmail,
                    SubjectType = "FinanceDistribution",
                    SubjectId = result.DistributionId?.ToString(),
                    CorrelationId = context.CorrelationId(),
                    ClientIp = context.ClientIp(),
                    UserAgent = context.ClientUserAgent(),
                    Reason = request.Reason,
                },
                cancellationToken);
        }

        return result.Outcome switch
        {
            FinanceDistributionOutcome.Executed or FinanceDistributionOutcome.ReplayedExistingExecution =>
                Results.Ok(result),
            FinanceDistributionOutcome.PlanChanged => Results.Problem(
                title: "The plan changed since preview",
                detail: "Recompute a preview and confirm again.",
                statusCode: StatusCodes.Status409Conflict),
            FinanceDistributionOutcome.AlreadyDistributedForPeriod => Results.Problem(
                title: "This period was already distributed",
                statusCode: StatusCodes.Status409Conflict),
            FinanceDistributionOutcome.InsufficientSourceBalance => Results.Problem(
                title: "Insufficient source balance",
                statusCode: StatusCodes.Status409Conflict),
            FinanceDistributionOutcome.ConfirmationPhraseMismatch => ValidationProblem(
                "The confirmation phrase does not match the distributable amount."),
            FinanceDistributionOutcome.NothingToDistribute => ValidationProblem(
                "There is no profit to distribute for this period."),
            FinanceDistributionOutcome.SharesDoNotSumToTotal => ValidationProblem(
                "Eligible partner shares do not sum to 10000 basis points."),
            FinanceDistributionOutcome.NoEligiblePartners => ValidationProblem(
                "No active holder has a nonzero distribution share."),
            FinanceDistributionOutcome.SourceAccountNotFound => Results.Problem(
                title: "Account not found",
                statusCode: StatusCodes.Status404NotFound),
            FinanceDistributionOutcome.SourceAccountClosed => Results.Problem(
                title: "Account is closed",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> ReverseAsync(
        Guid distributionId,
        ReverseFinanceDistributionRequest request,
        ClaimsPrincipal principal,
        FinanceDistributionService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ValidationProblem("A reason is required to reverse a distribution.");
        }

        FinanceDistributionResult result = await service.ReverseAsync(
            distributionId,
            request.Reason,
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            null,
            cancellationToken);

        return result.Outcome switch
        {
            FinanceDistributionOutcome.Reversed => Results.Ok(result),
            FinanceDistributionOutcome.NotFound => Results.Problem(
                title: "Distribution not found",
                statusCode: StatusCodes.Status404NotFound),
            FinanceDistributionOutcome.AlreadyReversed => Results.Problem(
                title: "Distribution already reversed",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static IResult ValidationProblem(string detail) => Results.Problem(
        title: "Invalid request",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);
}
