using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.Scheduling.Diffing;

/// <summary>
/// Loads the two revisions a diff compares and stores the result exactly once.
/// </summary>
public interface IScheduleDiffStore
{
    /// <summary>
    /// Loads the records needed to diff one published revision.
    /// </summary>
    /// <returns>
    /// The input, or <see langword="null"/> when the revision has never been
    /// published or already has a diff. Both are normal: publication and diff
    /// calculation are separate steps and either may be repeated after a crash.
    /// </returns>
    Task<ScheduleDiffInput?> LoadAsync(Guid revisionId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists revisions that reached publication but have no diff yet, oldest first.
    /// </summary>
    /// <remarks>
    /// A revision that was published and then superseded before its diff was
    /// calculated is still included. Skipping it would silently drop everything
    /// that revision changed.
    /// </remarks>
    Task<IReadOnlyList<Guid>> ListPendingDiffAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Stores a diff and its entries in one transaction.</summary>
    Task<ScheduleDiffPersistenceResult> SaveAsync(
        ScheduleDiff diff,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists diffs waiting to be fanned out onto student calendars (ADR-059): dispatchable, still
    /// <see cref="CalendarDispatchState.Pending"/>, and past any back-off time, oldest first.
    /// </summary>
    /// <param name="now">The current time, so a deferred diff is only returned once its retry is due.</param>
    Task<IReadOnlyList<Guid>> ListPendingDispatchAsync(
        int limit,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads one diff's entries for dispatch, as read-only data (ADR-059). Returns
    /// <see langword="null"/> when the diff no longer exists or is no longer dispatch-pending, both of
    /// which a resumed pass treats as "nothing to do".
    /// </summary>
    Task<DispatchableDiff?> LoadForDispatchAsync(Guid diffId, CancellationToken cancellationToken);

    /// <summary>Marks a diff fully applied to calendars (ADR-059), under optimistic concurrency.</summary>
    Task MarkDispatchedAsync(Guid diffId, DateTimeOffset atUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a dispatch attempt failed transiently, deferring the diff with a back-off or
    /// moving it to <see cref="CalendarDispatchState.Failed"/> once attempts are exhausted (ADR-059).
    /// </summary>
    /// <returns>The resulting dispatch state, so the caller can report deferral versus giving up.</returns>
    Task<CalendarDispatchState> RecordDispatchFailureAsync(
        Guid diffId,
        string reason,
        TimeSpan baseRetryDelay,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists dispatchable semantic diffs already applied globally after one user's ordered
    /// reconciliation cursor (ADR-060). These immutable diffs are the only authority for replay
    /// deletions; current-state absence is never queried as a substitute.
    /// </summary>
    Task<IReadOnlyList<DispatchedDiff>> ListDispatchedForReplayAsync(
        DateTimeOffset afterDispatchedAtUtc,
        Guid afterDiffId,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// One diff's entries as read-only data for incremental dispatch. It is deliberately detached: the
/// service does its Google I/O against this snapshot and records the outcome through a separate,
/// freshly-loaded transition, so a change-tracker reset mid-fan-out cannot strand the aggregate.
/// </summary>
public sealed record DispatchableDiff
{
    public required Guid DiffId { get; init; }

    /// <summary>The source that produced these records, for scoping the ledger reverse lookup.</summary>
    public required Domain.Scheduling.Sources.SourceId SourceId { get; init; }

    public required IReadOnlyList<ScheduleDiffEntry> Entries { get; init; }
}

/// <summary>
/// One globally completed diff admitted for a single user's reconciliation replay.
/// </summary>
public sealed record DispatchedDiff
{
    public required Guid DiffId { get; init; }

    public required Domain.Scheduling.Sources.SourceId SourceId { get; init; }

    /// <summary>The first half of the durable replay ordering key.</summary>
    public required DateTimeOffset DispatchedAtUtc { get; init; }

    public required IReadOnlyList<ScheduleDiffEntry> Entries { get; init; }
}

public sealed record ScheduleDiffInput
{
    public required Guid ScheduleSourceId { get; init; }

    public required Domain.Scheduling.Sources.SourceId SourceId { get; init; }

    public required Guid CurrentRevisionId { get; init; }

    public Guid? PreviousRevisionId { get; init; }

    /// <summary>The superseded revision's records, empty for a first publication.</summary>
    public required IReadOnlyList<CanonicalScheduleRecord> PreviousRecords { get; init; }

    public required IReadOnlyList<CanonicalScheduleRecord> CurrentRecords { get; init; }
}

public sealed record ScheduleDiffPersistenceResult
{
    public required ScheduleDiffPersistenceOutcome Outcome { get; init; }

    /// <summary>The stored diff's identifier, or the existing one's when it was already stored.</summary>
    public Guid? ScheduleDiffId { get; init; }
}

public enum ScheduleDiffPersistenceOutcome
{
    Stored,

    /// <summary>Another pass stored a diff for this revision first.</summary>
    AlreadyCalculated,
}
