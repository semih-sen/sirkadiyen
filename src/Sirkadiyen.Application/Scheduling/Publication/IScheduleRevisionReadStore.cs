using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.Scheduling.Publication;

/// <summary>
/// Reads revisions and the findings behind their state, for an operator deciding
/// whether to approve one.
/// </summary>
/// <remarks>
/// Approving a revision without seeing why it was held would be rubber-stamping,
/// so the review queue and the evidence are part of the same feature as the
/// approval itself rather than a later convenience.
/// </remarks>
public interface IScheduleRevisionReadStore
{
    Task<IReadOnlyList<ScheduleRevisionSummary>> ListByStateAsync(
        RevisionState state,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the most recent revisions in any state, newest first, optionally for one source
    /// (ADR-127).
    /// </summary>
    /// <remarks>
    /// <see cref="ListByStateAsync"/> only surfaces one state at a time — used for the review queue.
    /// This is the history view: published, superseded and rejected revisions together, so an
    /// operator can see what a source has produced over time.
    /// </remarks>
    Task<IReadOnlyList<ScheduleRevisionSummary>> ListRecentAsync(
        int limit,
        string? sourceId,
        CancellationToken cancellationToken);

    Task<ScheduleRevisionDetail?> FindAsync(
        Guid revisionId,
        CancellationToken cancellationToken);
}

public sealed record ScheduleRevisionSummary
{
    public required Guid RevisionId { get; init; }

    public required string SourceId { get; init; }

    public required RevisionState State { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required int RecordCount { get; init; }

    public string? StateReason { get; init; }
}

public sealed record ScheduleRevisionDetail
{
    public required ScheduleRevisionSummary Summary { get; init; }

    public required IReadOnlyList<RevisionFindingView> Findings { get; init; }

    public string? ApprovedBy { get; init; }

    public string? ApprovalReason { get; init; }

    public DateTimeOffset? ApprovedAtUtc { get; init; }

    public DateTimeOffset? PublishedAtUtc { get; init; }

    /// <summary>
    /// Who closed the review terminally, and why (ADR-097). Never the approval fields: a
    /// rejection recorded under <see cref="ApprovedBy"/> would state the opposite of what
    /// happened.
    /// </summary>
    /// <remarks>
    /// Read back here because rejection is terminal. Once the revision leaves
    /// <c>ReviewRequired</c> the queue no longer lists it, so without these the reason it never
    /// reached a calendar would be unrecoverable from any operator surface.
    /// </remarks>
    public string? RejectedBy { get; init; }

    public string? RejectionReason { get; init; }

    public DateTimeOffset? RejectedAtUtc { get; init; }
}

public sealed record RevisionFindingView
{
    public required RevisionValidationRule Rule { get; init; }

    public required ValidationSeverity Severity { get; init; }

    public required string Message { get; init; }

    public required int AffectedRecordCount { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>The JSON evidence the rule recorded, or empty.</summary>
    public required string Detail { get; init; }
}
