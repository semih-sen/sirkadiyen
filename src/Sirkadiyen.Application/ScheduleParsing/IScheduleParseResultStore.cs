using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.ScheduleParsing;

/// <summary>
/// Owns the transactional persistence boundary between a parser response and
/// the candidate revision it produced.
/// </summary>
public interface IScheduleParseResultStore
{
    Task<BeginParseRunResult> BeginOrResumeAsync(
        SourceSnapshot snapshot,
        ScheduleSource source,
        string correlationId,
        DateTimeOffset startedAtUtc,
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
}
