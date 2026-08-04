using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using ParserWarning = Sirkadiyen.Contracts.Parsing.ParserWarning;

namespace Sirkadiyen.Application.Administration;

/// <summary>
/// Read-only admin views over the ingestion pipeline's per-source health: poll status, the latest
/// parse run (with its warning/error counts), and the latest revision — plus a per-source detail
/// with recent snapshots. It reads existing pipeline tables; it never triggers polling or parsing.
/// </summary>
public interface ISourceStatusReadStore
{
    Task<IReadOnlyList<SourceStatusListItem>> ListAsync(CancellationToken cancellationToken);

    Task<SourceStatusDetail?> FindAsync(string sourceId, CancellationToken cancellationToken);
}

public sealed record SourceStatusListItem
{
    public required string SourceId { get; init; }

    public required string DisplayName { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required ScheduleSourceTransport Transport { get; init; }

    public required bool IsPollingEnabled { get; init; }

    public DateTimeOffset? LastPolledAtUtc { get; init; }

    public DateTimeOffset? LastChangedAtUtc { get; init; }

    public ParseRunStatus? LatestParseRunStatus { get; init; }

    public DateTimeOffset? LatestParseRunAtUtc { get; init; }

    public int? LatestParseWarningCount { get; init; }

    public int? LatestParseErrorCount { get; init; }

    public Guid? LatestRevisionId { get; init; }

    public RevisionState? LatestRevisionState { get; init; }

    public DateTimeOffset? LatestRevisionAtUtc { get; init; }
}

public sealed record SourceStatusDetail
{
    public required SourceStatusListItem Summary { get; init; }

    public required string ParserProfile { get; init; }

    public required string ParserProfileVersion { get; init; }

    /// <summary>
    /// The warnings returned by the latest completed parser run. These are read from the
    /// persisted parser response; opening this view never invokes the parser.
    /// </summary>
    public required IReadOnlyList<ParserWarning> LatestParseWarnings { get; init; }

    public required IReadOnlyList<SourceSnapshotSummary> RecentSnapshots { get; init; }
}

public sealed record SourceSnapshotSummary
{
    public required Guid SnapshotId { get; init; }

    public required DateTimeOffset AcquiredAtUtc { get; init; }

    public required string ContentHash { get; init; }

    public required int WorksheetCount { get; init; }

    public required int CellCount { get; init; }

    public required int DiagnosticCount { get; init; }

    /// <summary>Whether the normalized payload is still retained (not pruned; ADR-044).</summary>
    public required bool HasPayload { get; init; }
}
