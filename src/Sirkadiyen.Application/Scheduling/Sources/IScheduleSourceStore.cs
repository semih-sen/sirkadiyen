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
    /// Applies a whole catalog: upserts every source it declares and retires every persisted
    /// source it no longer declares (ADR-155).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="UpsertAsync"/> because it is the one operation that may act on
    /// rows the caller did not name, so a caller holding only part of the catalog cannot reach it
    /// by accident. It is applied on every worker start rather than only when the document
    /// changes: a source dropped by an earlier release must still end up retired on a server that
    /// has been running since before this existed.
    /// </remarks>
    Task<ScheduleSourceCatalogApplication> ApplyCatalogAsync(
        IReadOnlyCollection<ScheduleSource> sources,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records that a cycle completed for a source whose document is not fetched (ADR-155).
    /// </summary>
    /// <remarks>
    /// An administratively uploaded source acquires nothing during a poll, so the acquisition path
    /// — the only writer of a successful poll — never runs for it. Its poll time therefore stood
    /// still at the upload that stored its evidence, and, worse, a single failed cycle stayed on
    /// the row for good: nothing could ever clear it, so the panel said "belge alınamıyor" about a
    /// source that was being parsed and published successfully every cycle since.
    /// </remarks>
    Task RecordPollCompletedAsync(
        SourceId sourceId,
        DateTimeOffset polledAtUtc,
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

/// <summary>What applying a whole catalog did to the persisted sources (ADR-155).</summary>
/// <remarks>
/// The two lists are reported rather than only counted because they are the log line an operator
/// reads after a deployment: which sources stopped being configured, and which came back.
/// </remarks>
public sealed record ScheduleSourceCatalogApplication
{
    /// <summary>Rows the catalog inserted or changed.</summary>
    public required int RowsChanged { get; init; }

    /// <summary>Sources this application retired, having found no declaration for them.</summary>
    public required IReadOnlyList<SourceId> Retired { get; init; }

    /// <summary>Retired sources the catalog declares again, now polled once more.</summary>
    public required IReadOnlyList<SourceId> Reinstated { get; init; }
}
