using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.ScheduleSources;

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
}
