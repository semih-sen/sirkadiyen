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
            .AddEndpointFilter<AdminApiKeyFilter>()
            .WithTags("Revisions");

        revisions.MapGet("/", ListAsync)
            .WithSummary("Lists revisions in one state, oldest first.");

        revisions.MapGet("/{id:guid}", FindAsync)
            .WithSummary("Returns one revision with the findings behind its state.");

        revisions.MapPost("/{id:guid}/approve", ApproveAsync)
            .WithSummary("Approves a quarantined revision and publishes it.");

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
        ScheduleRevisionPublicationService publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string approvedBy = request.ApprovedBy?.Trim() ?? string.Empty;
        string approvalReason = request.ApprovalReason?.Trim() ?? string.Empty;

        if (approvedBy.Length == 0 || approvalReason.Length == 0)
        {
            // Both are the whole point of the endpoint. An approval that does not
            // say who made it and why is not an audit trail.
            return Results.Problem(
                title: "Incomplete approval",
                detail: "'approvedBy' and 'approvalReason' are both required.",
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
}

/// <summary>
/// Who is approving a quarantined revision, and why.
/// </summary>
/// <remarks>
/// The identity is supplied by the caller because there is no identity provider
/// yet. It is therefore a record of a claim, not a verified one; the API key
/// establishes that the caller is an operator, not which operator they are.
/// </remarks>
public sealed record ApproveRevisionRequest
{
    /// <example>semih</example>
    public required string? ApprovedBy { get; init; }

    /// <example>Checked the source: the 40% drop is the exam period, not a parse fault.</example>
    public required string? ApprovalReason { get; init; }
}

public sealed record ApproveRevisionResponse
{
    public required Guid RevisionId { get; init; }

    public required bool Approved { get; init; }

    public required RevisionPublicationOutcome PublicationOutcome { get; init; }

    public Guid? SupersededRevisionId { get; init; }
}
