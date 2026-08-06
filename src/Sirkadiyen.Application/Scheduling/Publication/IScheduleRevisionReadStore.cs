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
