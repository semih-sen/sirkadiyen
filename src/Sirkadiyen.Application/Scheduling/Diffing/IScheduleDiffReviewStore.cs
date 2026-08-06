using Sirkadiyen.Domain.ScheduleDiffing;

namespace Sirkadiyen.Application.ScheduleDiffing;

/// <summary>
/// Reads the held-diff queue and records an operator releasing one (ADR-042).
/// </summary>
/// <remarks>
/// The evidence and the release live behind the same port for the same reason
/// the revision review queue does: releasing a diff without seeing which lessons
/// it deletes is rubber-stamping, and this is the last gate before a student's
/// calendar changes.
/// </remarks>
public interface IScheduleDiffReviewStore
{
    Task<IReadOnlyList<ScheduleDiffSummary>> ListByStateAsync(
        ScheduleDiffState state,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns one diff with the entries an operator has to weigh, capped at
    /// <paramref name="entryLimit"/>.
    /// </summary>
    /// <remarks>
    /// Only the entries that change a calendar are returned, newest-affecting
    /// first: unchanged entries are the overwhelming majority and say nothing
    /// about whether the hold is legitimate.
    /// </remarks>
    Task<ScheduleDiffDetail?> FindAsync(
        Guid scheduleDiffId,
        int entryLimit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists diffs by how far their calendar fan-out has progressed, oldest first (ADR-097).
    /// </summary>
    /// <remarks>
    /// Dispatch state is orthogonal to <see cref="ScheduleDiffState"/>: a diff whose fan-out failed
    /// terminally is still <c>Ready</c> or <c>Released</c>, so the state-based queue cannot find it.
    /// Without this the failed queue would be unenumerable and the retry route unusable.
    /// </remarks>
    Task<IReadOnlyList<ScheduleDiffSummary>> ListByDispatchStateAsync(
        CalendarDispatchState dispatchState,
        int limit,
        CancellationToken cancellationToken);

    Task<ScheduleDiffReleaseResult> ReleaseAsync(
        Guid scheduleDiffId,
        string releasedBy,
        string releaseReason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a terminally failed diff to the dispatch queue on a named operator's behalf
    /// (ADR-097).
    /// </summary>
    Task<ScheduleDiffRetryResult> RetryDispatchAsync(
        Guid scheduleDiffId,
        string retriedBy,
        string retryReason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

public sealed record ScheduleDiffSummary
{
    public required Guid ScheduleDiffId { get; init; }

    public required string SourceId { get; init; }

    public required ScheduleDiffState State { get; init; }

    public required Guid CurrentRevisionId { get; init; }

    public Guid? PreviousRevisionId { get; init; }

    public required int CreatedCount { get; init; }

    public required int UpdatedCount { get; init; }

    public required int DeletedCount { get; init; }

    public required int UnchangedCount { get; init; }

    public required int AmbiguousCount { get; init; }

    public required int PreviousRecordCount { get; init; }

    public required int CurrentRecordCount { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public string? HoldReason { get; init; }

    /// <summary>Whether an operator may release it, or only the source can fix it.</summary>
    public required bool IsReleasable { get; init; }

    public string? ReleasedBy { get; init; }

    public string? ReleaseReason { get; init; }

    public DateTimeOffset? ReleasedAtUtc { get; init; }

    /// <summary>How far the fan-out onto student calendars has progressed (ADR-059).</summary>
    public required CalendarDispatchState CalendarDispatchState { get; init; }

    public required int DispatchAttempts { get; init; }

    public DateTimeOffset? DispatchedAtUtc { get; init; }

    /// <summary>Why the last dispatch attempt failed; the operator's evidence for a retry.</summary>
    public string? DispatchFailureReason { get; init; }

    /// <summary>Whether an operator may return this diff to the dispatch queue (ADR-097).</summary>
    public required bool IsDispatchRetriable { get; init; }

    /// <summary>How many times an operator has already retried it.</summary>
    public required int DispatchRetryCount { get; init; }

    public string? LastDispatchRetriedBy { get; init; }

    public string? LastDispatchRetryReason { get; init; }

    public DateTimeOffset? LastDispatchRetriedAtUtc { get; init; }
}

public sealed record ScheduleDiffDetail
{
    public required ScheduleDiffSummary Summary { get; init; }

    public required IReadOnlyList<ScheduleDiffEntryView> Entries { get; init; }

    /// <summary>How many actionable entries exist, which may exceed the returned ones.</summary>
    public required int ActionableEntryCount { get; init; }
}

/// <summary>
/// One diff entry described in the terms an operator recognizes: the lesson, not
/// the record identifier.
/// </summary>
public sealed record ScheduleDiffEntryView
{
    public required ScheduleDiffChange Change { get; init; }

    public required ScheduleDiffMatch Match { get; init; }

    public decimal? MatchScore { get; init; }

    /// <summary>The lesson as the superseded revision published it, if any.</summary>
    public ScheduleDiffRecordView? Previous { get; init; }

    /// <summary>The lesson as the current revision publishes it, if any.</summary>
    public ScheduleDiffRecordView? Current { get; init; }
}

public sealed record ScheduleDiffRecordView
{
    public required Guid RecordId { get; init; }

    public required string DisplayTitle { get; init; }

    public required DateOnly LocalDate { get; init; }

    /// <summary>The local times, or <c>null</c> on both for an all-day item.</summary>
    /// <remarks>
    /// An operator reading the hold queue has to be able to tell a holiday from a
    /// lesson whose times went missing, so the shape is shown rather than filled in
    /// with midnight.
    /// </remarks>
    public TimeOnly? StartLocalTime { get; init; }

    public TimeOnly? EndLocalTime { get; init; }

    public required bool IsAllDay { get; init; }

    public required string AudienceSelectors { get; init; }

    public string? Instructor { get; init; }

    public string? Location { get; init; }
}

public sealed record ScheduleDiffReleaseResult
{
    public required ScheduleDiffReleaseOutcome Outcome { get; init; }

    /// <summary>The state observed when the release was refused.</summary>
    public ScheduleDiffState? ObservedState { get; init; }

    public DateTimeOffset? ReleasedAtUtc { get; init; }
}

public enum ScheduleDiffReleaseOutcome
{
    Released,

    DiffNotFound,

    /// <summary>The diff was never held, or somebody released it first.</summary>
    NotHeld,

    /// <summary>
    /// The hold is ambiguity. No operator may wave it through; the source has to
    /// state which lesson is which (ADR-042).
    /// </summary>
    AmbiguityMustBeResolvedAtSource,

    /// <summary>Another operator changed the diff during this release.</summary>
    ConcurrentRelease,
}

public sealed record ScheduleDiffRetryResult
{
    public required ScheduleDiffRetryOutcome Outcome { get; init; }

    /// <summary>The dispatch state observed when the retry was refused.</summary>
    public CalendarDispatchState? ObservedDispatchState { get; init; }

    public DateTimeOffset? RetriedAtUtc { get; init; }

    /// <summary>How many times this diff has now been retried, including this one.</summary>
    public int DispatchRetryCount { get; init; }
}

public enum ScheduleDiffRetryOutcome
{
    Retried,

    DiffNotFound,

    /// <summary>
    /// The dispatch has not failed terminally, so there is nothing to retry: a pending diff is
    /// already queued and a dispatched one is done.
    /// </summary>
    NotFailed,

    /// <summary>
    /// The diff is held, so it may not reach a calendar at all. Releasing it is the operator
    /// action that applies (ADR-042), not retrying it.
    /// </summary>
    NotDispatchable,

    /// <summary>Another operator or a worker changed the diff during this retry.</summary>
    ConcurrentChange,
}
