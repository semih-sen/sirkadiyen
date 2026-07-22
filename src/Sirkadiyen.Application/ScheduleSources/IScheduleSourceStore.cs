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
    /// Inserts sources that do not exist yet and updates the ones that do,
    /// keeping the persisted catalog in step with the configured one.
    /// </summary>
    Task<int> UpsertAsync(
        IReadOnlyCollection<ScheduleSource> sources,
        CancellationToken cancellationToken);
}
