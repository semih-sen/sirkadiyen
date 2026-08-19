using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Sources;

/// <summary>The append-only history of the schedule source catalog document (ADR-114).</summary>
public interface IScheduleSourceCatalogRevisionStore
{
    /// <summary>
    /// Persists one revision, upserts the sources it configures and disables polling for the ones
    /// it no longer declares, in a single transaction. Revision rows are never updated or deleted.
    /// </summary>
    /// <returns>How many persisted source rows were inserted or updated.</returns>
    Task<int> CommitAsync(
        ScheduleSourceCatalogCommit commit,
        CancellationToken cancellationToken);

    /// <summary>Returns the newest revisions first, without their content.</summary>
    Task<IReadOnlyList<ScheduleSourceCatalogRevisionSummary>> ListAsync(
        int limit,
        string currentContentHash,
        CancellationToken cancellationToken);

    /// <summary>Returns one revision with its full document.</summary>
    Task<ScheduleSourceCatalogRevisionDetail?> FindAsync(
        Guid id,
        string currentContentHash,
        CancellationToken cancellationToken);
}
