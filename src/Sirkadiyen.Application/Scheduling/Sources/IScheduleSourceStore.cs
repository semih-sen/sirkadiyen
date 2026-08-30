using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Sources;

/// <summary>
/// Reads and updates the configured schedule sources.
/// </summary>
public interface IScheduleSourceStore
{
    Task<IReadOnlyList<ScheduleSource>> ListAsync(
        bool onlyPollingEnabled,
        CancellationToken cancellationToken);

    Task<ScheduleSource?> FindAsync(SourceId sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// The sources served by the same document as this one, including it, in a
    /// deterministic order.
    /// </summary>
    /// <remarks>
    /// A source that declares no shared-document group is its own only member, so
    /// a caller never has to special-case the ordinary one-source case (ADR-080).
    /// </remarks>
    Task<IReadOnlyList<ScheduleSource>> ListSharingDocumentAsync(
        SourceId sourceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts sources that do not exist yet and updates the ones that do,
    /// keeping the persisted catalog in step with the configured one.
    /// </summary>
    Task<int> UpsertAsync(
        IReadOnlyCollection<ScheduleSource> sources,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records that a poll could not acquire the source's document (ADR-137).
    /// </summary>
    /// <remarks>
    /// Separate from the poll itself because it is written on the path where the poll threw, and
    /// it must not be swallowed by whatever went wrong: a failure nobody can see is the reason
    /// this exists. The previous successful poll is left intact.
    /// </remarks>
    Task RecordPollFailureAsync(
        SourceId sourceId,
        DateTimeOffset failedAtUtc,
        string reason,
        CancellationToken cancellationToken);
}
