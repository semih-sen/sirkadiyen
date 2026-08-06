using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// The internal endpoints an operator needs to work the review queue while there
/// is no administration frontend (ADR-032).
/// </summary>
public static class RevisionEndpoints
{
    private const int MaximumListLimit = 200;

    public static IEndpointRouteBuilder MapRevisionEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder revisions = builder
            .MapGroup("/api/revisions")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Revisions");

        revisions.MapGet("/", ListAsync)
            .WithSummary("Lists revisions in one state, oldest first.");

        revisions.MapGet("/{id:guid}", FindAsync)
            .WithSummary("Returns one revision with the findings behind its state.");

        revisions.MapPost("/{id:guid}/approve", ApproveAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Approves a quarantined revision and publishes it.");

        revisions.MapPost("/{id:guid}/reject", RejectAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Rejects a quarantined revision, closing its review terminally.");

        return builder;
    }

    private static async Task<IResult> ListAsync(
        IScheduleRevisionReadStore store,
        CancellationToken cancellationToken,
        RevisionState state = RevisionState.ReviewRequired,
        int limit = 50)
    {
        if (limit is < 1 or > MaximumListLimit)
        {
            return Results.Problem(
                title: "Invalid limit",
                detail: $"'limit' must be between 1 and {MaximumListLimit}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(await store.ListByStateAsync(state, limit, cancellationToken));
    }

    private static async Task<IResult> FindAsync(
        Guid id,
        IScheduleRevisionReadStore store,
        CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : Results.Problem(
                title: "Revision not found",
                detail: $"No revision with ID '{id}' exists.",
                statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> ApproveAsync(
        Guid id,
        ApproveRevisionRequest request,
        ClaimsPrincipal principal,
        ScheduleRevisionPublicationService publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string approvedBy = UserClaimsPrincipalFactory.GetRequiredEmail(principal);
        string approvalReason = request.ApprovalReason?.Trim() ?? string.Empty;

        if (approvalReason.Length == 0)
        {
            // Both are the whole point of the endpoint. An approval that does not
            // say who made it and why is not an audit trail.
            return Results.Problem(
                title: "Incomplete approval",
                detail: "'approvalReason' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Checked here as well as in the domain, so an over-long field is a 400
        // rather than an unhandled exception on the way to the database.
        if (approvedBy.Length > ScheduleRevision.MaximumApprovedByLength
            || approvalReason.Length > ScheduleRevision.MaximumApprovalReasonLength)
        {
            return Results.Problem(
                title: "Approval fields are too long",
                detail: $"'approvedBy' allows {ScheduleRevision.MaximumApprovedByLength} "
                    + $"characters and 'approvalReason' allows "
                    + $"{ScheduleRevision.MaximumApprovalReasonLength}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        RevisionApprovalOutcomeResult result = await publication.ApproveAndPublishAsync(
            id,
            approvedBy,
            approvalReason,
            cancellationToken);

        return result.Approval.Outcome switch
        {
            RevisionApprovalOutcome.RevisionNotFound => Results.Problem(
                title: "Revision not found",
                detail: $"No revision with ID '{id}' exists.",
                statusCode: StatusCodes.Status404NotFound),

            RevisionApprovalOutcome.NotAwaitingReview => Results.Problem(
                title: "Revision is not awaiting review",
                detail: $"The revision is {result.Approval.ObservedState}, so there is "
                    + "nothing to approve.",
                statusCode: StatusCodes.Status409Conflict),

            _ => Results.Ok(new ApproveRevisionResponse
            {
                RevisionId = id,
                Approved = true,
                PublicationOutcome = result.Publication?.Outcome
                    ?? RevisionPublicationOutcome.NotValidated,
                SupersededRevisionId = result.Publication?.SupersededRevisionId,
            }),
        };
    }

    private static async Task<IResult> RejectAsync(
        Guid id,
        RejectRevisionRequest request,
        ClaimsPrincipal principal,
        ScheduleRevisionPublicationService publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string rejectedBy = UserClaimsPrincipalFactory.GetRequiredEmail(principal);
        string rejectionReason = request.RejectionReason?.Trim() ?? string.Empty;

        if (rejectionReason.Length == 0)
        {
            // Rejection is terminal, so the record of why is the only thing left explaining a
            // revision that never reached anyone's calendar.
            return Results.Problem(
                title: "Incomplete rejection",
                detail: "'rejectionReason' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (rejectedBy.Length > ScheduleRevision.MaximumRejectedByLength
            || rejectionReason.Length > ScheduleRevision.MaximumRejectionReasonLength)
        {
            return Results.Problem(
                title: "Rejection fields are too long",
                detail: $"'rejectedBy' allows {ScheduleRevision.MaximumRejectedByLength} "
                    + $"characters and 'rejectionReason' allows "
                    + $"{ScheduleRevision.MaximumRejectionReasonLength}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        RevisionRejectionResult result = await publication.RejectAsync(
            id,
            rejectedBy,
            rejectionReason,
            cancellationToken);

        return result.Outcome switch
        {
            RevisionRejectionOutcome.RevisionNotFound => Results.Problem(
                title: "Revision not found",
                detail: $"No revision with ID '{id}' exists.",
                statusCode: StatusCodes.Status404NotFound),

            RevisionRejectionOutcome.NotAwaitingReview => Results.Problem(
                title: "Revision is not awaiting review",
                detail: $"The revision is {result.ObservedState}, so there is nothing to reject. "
                    + "A published revision is never withdrawn; it is corrected by publishing a "
                    + "newer one over it.",
                statusCode: StatusCodes.Status409Conflict),

            _ => Results.Ok(new RejectRevisionResponse
            {
                RevisionId = id,
                Rejected = true,
            }),
        };
    }
}
