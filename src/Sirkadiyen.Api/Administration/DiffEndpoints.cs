using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleDiffing;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// The internal endpoints an operator needs to work the held-diff queue while
/// there is no administration frontend (ADR-042).
/// </summary>
public static class DiffEndpoints
{
    private const int MaximumListLimit = 200;

    private const int MaximumEntryLimit = 500;

    public static IEndpointRouteBuilder MapDiffEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder diffs = builder
            .MapGroup("/api/diffs")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Diffs");

        diffs.MapGet("/", ListAsync)
            .WithSummary("Lists diffs in one state, oldest first.");

        diffs.MapGet("/{id:guid}", FindAsync)
            .WithSummary("Returns one diff with the changes behind its hold.");

        diffs.MapPost("/{id:guid}/release", ReleaseAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Releases a held diff for dispatch.");

        diffs.MapPost("/{id:guid}/retry", RetryAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Returns a terminally failed diff to the dispatch queue.");

        return builder;
    }

    /// <remarks>
    /// Two queues, one route. <c>state</c> answers "may this diff be acted on", which is the
    /// held-review queue; <c>dispatchState</c> answers "has it been", which is the only way to find
    /// a diff whose fan-out failed terminally — such a diff is still <c>Ready</c> or
    /// <c>Released</c>, so the state filter would never show it (ADR-097).
    /// </remarks>
    private static async Task<IResult> ListAsync(
        ScheduleDiffReviewService review,
        CancellationToken cancellationToken,
        ScheduleDiffState state = ScheduleDiffState.Held,
        CalendarDispatchState? dispatchState = null,
        int limit = 50)
    {
        if (limit is < 1 or > MaximumListLimit)
        {
            return Results.Problem(
                title: "Invalid limit",
                detail: $"'limit' must be between 1 and {MaximumListLimit}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(dispatchState is { } dispatch
            ? await review.ListByDispatchStateAsync(dispatch, limit, cancellationToken)
            : await review.ListAsync(state, limit, cancellationToken));
    }

    private static async Task<IResult> FindAsync(
        Guid id,
        ScheduleDiffReviewService review,
        CancellationToken cancellationToken,
        int entryLimit = 100)
    {
        if (entryLimit is < 1 or > MaximumEntryLimit)
        {
            return Results.Problem(
                title: "Invalid entry limit",
                detail: $"'entryLimit' must be between 1 and {MaximumEntryLimit}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await review.FindAsync(id, entryLimit, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : Results.Problem(
                title: "Diff not found",
                detail: $"No diff with ID '{id}' exists.",
                statusCode: StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ReleaseAsync(
        Guid id,
        ReleaseDiffRequest request,
        ClaimsPrincipal principal,
        ScheduleDiffReviewService review,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string releasedBy = UserClaimsPrincipalFactory.GetRequiredEmail(principal);
        string releaseReason = request.ReleaseReason?.Trim() ?? string.Empty;

        if (releaseReason.Length == 0)
        {
            // Both are the whole point of the endpoint. A release that does not
            // say who made it and why is not an audit trail.
            return Results.Problem(
                title: "Incomplete release",
                detail: "'releaseReason' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Checked here as well as in the domain, so an over-long field is a 400
        // rather than an unhandled exception on the way to the database.
        if (releasedBy.Length > ScheduleDiff.MaximumReleasedByLength
            || releaseReason.Length > ScheduleDiff.MaximumReleaseReasonLength)
        {
            return Results.Problem(
                title: "Release fields are too long",
                detail: $"'releasedBy' allows {ScheduleDiff.MaximumReleasedByLength} characters "
                    + $"and 'releaseReason' allows {ScheduleDiff.MaximumReleaseReasonLength}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        ScheduleDiffReleaseResult result = await review.ReleaseAsync(
            id,
            releasedBy,
            releaseReason,
            cancellationToken);

        return result.Outcome switch
        {
            ScheduleDiffReleaseOutcome.DiffNotFound => Results.Problem(
                title: "Diff not found",
                detail: $"No diff with ID '{id}' exists.",
                statusCode: StatusCodes.Status404NotFound),

            ScheduleDiffReleaseOutcome.NotHeld => Results.Problem(
                title: "Diff is not held",
                detail: $"The diff is {result.ObservedState}, so there is nothing to release.",
                statusCode: StatusCodes.Status409Conflict),

            ScheduleDiffReleaseOutcome.AmbiguityMustBeResolvedAtSource => Results.Problem(
                title: "Ambiguity cannot be released",
                detail: "The diff is held because it could not decide which record became "
                    + "which. Releasing it would leave the previous lesson in every affected "
                    + "calendar and never write its replacement, so the source has to state "
                    + "which lesson is which and a newer revision has to supersede this one.",
                statusCode: StatusCodes.Status409Conflict),

            ScheduleDiffReleaseOutcome.ConcurrentRelease => Results.Problem(
                title: "Diff changed during release",
                detail: "Another operator changed this diff. Read it again before releasing it.",
                statusCode: StatusCodes.Status409Conflict),

            _ => Results.Ok(new ReleaseDiffResponse
            {
                ScheduleDiffId = id,
                Released = true,
                ReleasedAtUtc = result.ReleasedAtUtc,
            }),
        };
    }

    private static async Task<IResult> RetryAsync(
        Guid id,
        RetryDiffRequest request,
        ClaimsPrincipal principal,
        ScheduleDiffReviewService review,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string retriedBy = UserClaimsPrincipalFactory.GetRequiredEmail(principal);
        string retryReason = request.RetryReason?.Trim() ?? string.Empty;

        if (retryReason.Length == 0)
        {
            return Results.Problem(
                title: "Incomplete retry",
                detail: "'retryReason' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (retriedBy.Length > ScheduleDiff.MaximumDispatchRetriedByLength
            || retryReason.Length > ScheduleDiff.MaximumDispatchRetryReasonLength)
        {
            return Results.Problem(
                title: "Retry fields are too long",
                detail: $"'retriedBy' allows {ScheduleDiff.MaximumDispatchRetriedByLength} "
                    + $"characters and 'retryReason' allows "
                    + $"{ScheduleDiff.MaximumDispatchRetryReasonLength}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        ScheduleDiffRetryResult result = await review.RetryDispatchAsync(
            id,
            retriedBy,
            retryReason,
            cancellationToken);

        return result.Outcome switch
        {
            ScheduleDiffRetryOutcome.DiffNotFound => Results.Problem(
                title: "Diff not found",
                detail: $"No diff with ID '{id}' exists.",
                statusCode: StatusCodes.Status404NotFound),

            ScheduleDiffRetryOutcome.NotFailed => Results.Problem(
                title: "Diff dispatch has not failed",
                detail: $"The diff's dispatch is {result.ObservedDispatchState}, so there is "
                    + "nothing to retry: a pending diff is already queued and a dispatched one "
                    + "is done.",
                statusCode: StatusCodes.Status409Conflict),

            ScheduleDiffRetryOutcome.NotDispatchable => Results.Problem(
                title: "Diff may not reach a calendar",
                detail: "The diff is held, so retrying it would release it under another name. "
                    + "Release it explicitly if an operator can take responsibility for it, or "
                    + "correct the source and let a newer revision supersede it.",
                statusCode: StatusCodes.Status409Conflict),

            ScheduleDiffRetryOutcome.ConcurrentChange => Results.Problem(
                title: "Diff changed during retry",
                detail: "A worker or another operator changed this diff. Read it again before "
                    + "retrying it.",
                statusCode: StatusCodes.Status409Conflict),

            _ => Results.Ok(new RetryDiffResponse
            {
                ScheduleDiffId = id,
                Retried = true,
                RetriedAtUtc = result.RetriedAtUtc,
                DispatchRetryCount = result.DispatchRetryCount,
            }),
        };
    }
}

/// <summary>
/// Why the authenticated SuperAdmin is returning a failed diff to the dispatch queue.
/// </summary>
/// <remarks>
/// The actor is derived from the backend-authenticated Google identity and is
/// never accepted from this payload.
/// </remarks>
public sealed record RetryDiffRequest
{
    /// <example>The Calendar outage that exhausted the attempts is over; Google is answering again.</example>
    public required string? RetryReason { get; init; }
}

public sealed record RetryDiffResponse
{
    public required Guid ScheduleDiffId { get; init; }

    public required bool Retried { get; init; }

    public DateTimeOffset? RetriedAtUtc { get; init; }

    /// <summary>How many times this diff has now been retried, including this one.</summary>
    public required int DispatchRetryCount { get; init; }
}

/// <summary>
/// Why the authenticated SuperAdmin is releasing a held diff.
/// </summary>
/// <remarks>
/// The actor is derived from the backend-authenticated Google identity and is
/// never accepted from this payload.
/// </remarks>
public sealed record ReleaseDiffRequest
{
    /// <example>Checked the source: the 38 deleted lessons are the ended anatomy block.</example>
    public required string? ReleaseReason { get; init; }
}

public sealed record ReleaseDiffResponse
{
    public required Guid ScheduleDiffId { get; init; }

    public required bool Released { get; init; }

    public DateTimeOffset? ReleasedAtUtc { get; init; }
}
