using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Parsing;

/// <summary>
/// Owns the transactional persistence boundary between a parser response and
/// the candidate revision it produced.
/// </summary>
public interface IScheduleParseResultStore
{
    /// <param name="staleRunTimeout">
    /// How long a run may stay open before it is treated as abandoned by a worker
    /// that no longer exists. The policy is the caller's, not the store's.
    /// </param>
    Task<BeginParseRunResult> BeginOrResumeAsync(
        SourceSnapshot snapshot,
        ScheduleSource source,
        string correlationId,
        DateTimeOffset startedAtUtc,
        TimeSpan staleRunTimeout,
        CancellationToken cancellationToken);

    Task<ScheduleRevision?> CompleteAsync(
        Guid parseRunId,
        ParseSnapshotResponse response,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid parseRunId,
        DateTimeOffset completedAtUtc,
        string failureReason,
        CancellationToken cancellationToken);
}

public sealed record BeginParseRunResult
{
    public required Guid ParseRunId { get; init; }

    public required ParseRunStatus Status { get; init; }

    public required bool ShouldInvokeParser { get; init; }

    /// <summary>How this run came to be open, for logging and poll reporting.</summary>
    public ParseRunStartKind StartKind { get; init; } = ParseRunStartKind.Started;
}

public enum ParseRunStartKind
{
    /// <summary>A new run, or one resumed after a recorded failure.</summary>
    Started,

    /// <summary>A run reopened after being left running by a vanished worker.</summary>
    RecoveredStale,
}
