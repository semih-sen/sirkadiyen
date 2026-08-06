using System.Globalization;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Domain.ScheduleDiffing;

/// <summary>
/// The stored semantic difference between a published revision and the revision
/// it superseded.
/// </summary>
/// <remarks>
/// A diff is the only authority for changing a student's calendar: deletion
/// requires a published revision and a valid semantic diff (AI_GUIDELINE section
/// 13). Storing it separates deciding what changed from acting on it, so the
/// decision is auditable, is calculated exactly once per revision, and can be
/// held back without losing it.
/// <para>
/// A diff is created in <see cref="ScheduleDiffState.Ready"/> or
/// <see cref="ScheduleDiffState.Held"/>. What it says about the two revisions
/// never changes; the one thing an operator may change is a held diff's state,
/// by releasing it (ADR-042). Correcting a bad publication remains forward-fix
/// only (ADR-033): the next revision produces the next diff.
/// </para>
/// </remarks>
public sealed class ScheduleDiff
{
    public const int MaximumHoldReasonLength = 2000;

    public const int MaximumReleasedByLength = 200;

    public const int MaximumReleaseReasonLength = 2000;

    public const int MaximumDispatchFailureReasonLength = 2000;

    public const int MaximumDispatchRetriedByLength = 200;

    public const int MaximumDispatchRetryReasonLength = 2000;

    private readonly List<ScheduleDiffEntry> entries = [];

    private ScheduleDiff()
    {
        // Materialization constructor.
    }

    /// <summary>
    /// Classifies a calculated diff and decides whether it may be dispatched.
    /// </summary>
    /// <param name="previousRevisionId">
    /// The revision this one superseded, or <see langword="null"/> when the
    /// source has just published for the first time. A null previous revision
    /// legitimately produces nothing but creations.
    /// </param>
    public static ScheduleDiff Create(
        Guid scheduleSourceId,
        SourceId sourceId,
        Guid? previousRevisionId,
        Guid currentRevisionId,
        IReadOnlyCollection<ScheduleDiffEntry> entries,
        ScheduleDiffSafetyThresholds thresholds,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(thresholds);
        thresholds.Validate();

        if (previousRevisionId == currentRevisionId)
        {
            throw new ArgumentException(
                "A revision cannot be diffed against itself.",
                nameof(previousRevisionId));
        }

        ScheduleDiff diff = new()
        {
            Id = Guid.CreateVersion7(),
            ScheduleSourceId = scheduleSourceId,
            SourceId = sourceId,
            PreviousRevisionId = previousRevisionId,
            CurrentRevisionId = currentRevisionId,
            CreatedAtUtc = createdAtUtc,
            CreatedCount = Count(entries, ScheduleDiffChange.Created),
            UpdatedCount = Count(entries, ScheduleDiffChange.Updated),
            DeletedCount = Count(entries, ScheduleDiffChange.Deleted),
            UnchangedCount = Count(entries, ScheduleDiffChange.Unchanged),
            AmbiguousCount = Count(entries, ScheduleDiffChange.Ambiguous),
            PreviousRecordCount = entries.Count(entry => entry.PreviousRecordId is not null),
            CurrentRecordCount = entries.Count(entry => entry.CurrentRecordId is not null),
        };

        diff.entries.AddRange(entries.Select(entry => entry with
        {
            Id = Guid.CreateVersion7(),
            ScheduleDiffId = diff.Id,
        }));

        string? holdReason = diff.DescribeHold(thresholds);
        diff.State = holdReason is null ? ScheduleDiffState.Ready : ScheduleDiffState.Held;
        diff.HoldReason = holdReason;
        diff.CalendarDispatchState = CalendarDispatchState.Pending;
        return diff;
    }

    public Guid Id { get; private init; }

    public Guid ScheduleSourceId { get; private init; }

    public SourceId SourceId { get; private init; }

    /// <summary>The superseded revision, or null for a source's first publication.</summary>
    public Guid? PreviousRevisionId { get; private init; }

    public Guid CurrentRevisionId { get; private init; }

    public ScheduleDiffState State { get; private set; }

    /// <summary>Why the diff was held, stated in full for the operator who reads it.</summary>
    public string? HoldReason { get; private set; }

    public int CreatedCount { get; private init; }

    public int UpdatedCount { get; private init; }

    public int DeletedCount { get; private init; }

    public int UnchangedCount { get; private init; }

    public int AmbiguousCount { get; private init; }

    /// <summary>How many records of the superseded revision the diff accounted for.</summary>
    public int PreviousRecordCount { get; private init; }

    public int CurrentRecordCount { get; private init; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    /// <summary>The operator who released a held diff, as a claimed identity.</summary>
    public string? ReleasedBy { get; private set; }

    public string? ReleaseReason { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    /// <summary>
    /// How far the fan-out of this diff onto student calendars has progressed (ADR-059). It is
    /// orthogonal to <see cref="State"/>: <see cref="State"/> says whether the diff <em>may</em>
    /// be acted on, this says whether it has been.
    /// </summary>
    public CalendarDispatchState CalendarDispatchState { get; private set; }

    /// <summary>How many times dispatch has been attempted and left the diff not fully applied.</summary>
    public int DispatchAttempts { get; private set; }

    /// <summary>
    /// The earliest time a deferred dispatch may be retried, set after a transient failure so the
    /// dispatcher backs off rather than re-running every cycle. Null when no retry is pending.
    /// </summary>
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    /// <summary>When every applicable calendar was updated from this diff.</summary>
    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    /// <summary>Why the last dispatch attempt failed, kept for the operator who reads a failed diff.</summary>
    public string? DispatchFailureReason { get; private set; }

    /// <summary>
    /// How many times an operator has returned this diff to the dispatch queue after it failed
    /// terminally (ADR-097). A diff retried repeatedly is an operational signal in its own right,
    /// so the count is kept rather than reset with the attempts.
    /// </summary>
    public int DispatchRetryCount { get; private set; }

    public string? LastDispatchRetriedBy { get; private set; }

    public string? LastDispatchRetryReason { get; private set; }

    public DateTimeOffset? LastDispatchRetriedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyList<ScheduleDiffEntry> Entries => entries;

    /// <summary>
    /// Whether a consumer may turn this diff into calendar operations.
    /// </summary>
    public bool IsDispatchable => State is ScheduleDiffState.Ready or ScheduleDiffState.Released;

    /// <summary>
    /// Whether this diff is waiting to be fanned out onto calendars: dispatchable, not yet
    /// dispatched, and not in a terminal failure.
    /// </summary>
    public bool IsDispatchPending =>
        IsDispatchable && CalendarDispatchState is CalendarDispatchState.Pending;

    /// <summary>
    /// Whether an operator may take responsibility for this diff.
    /// </summary>
    /// <remarks>
    /// A hold caused by ambiguity is deliberately not releasable. An operator can
    /// confirm that a large deletion is real by reading the source, but they
    /// cannot decide which of several candidates an ambiguous record became, and
    /// releasing it would leave the previous lesson in every affected calendar
    /// while its replacement is never written. That is fixed at the source
    /// (ADR-042).
    /// </remarks>
    public bool IsReleasable => State is ScheduleDiffState.Held && AmbiguousCount == 0;

    /// <summary>
    /// Records an operator taking responsibility for a held diff and allows it to
    /// be dispatched.
    /// </summary>
    /// <remarks>
    /// The hold and its reason are kept. A released diff reads as "held for this
    /// reason, then released by this person", which is the record that matters
    /// when someone later asks why a hundred lessons left a calendar.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The diff is not releasable.</exception>
    public void Release(string releasedBy, string releaseReason, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releasedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseReason);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            releasedBy.Length,
            MaximumReleasedByLength,
            nameof(releasedBy));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            releaseReason.Length,
            MaximumReleaseReasonLength,
            nameof(releaseReason));

        if (!IsReleasable)
        {
            throw new InvalidOperationException(
                AmbiguousCount > 0
                    ? "An ambiguous diff cannot be released; the ambiguity is resolved at the source."
                    : $"A diff in {State} cannot be released.");
        }

        State = ScheduleDiffState.Released;
        ReleasedBy = releasedBy;
        ReleaseReason = releaseReason;
        ReleasedAtUtc = atUtc;
    }

    /// <summary>
    /// Records that every applicable student calendar has been updated from this diff (ADR-059).
    /// </summary>
    /// <remarks>
    /// Dispatch is idempotent — the caller re-runs the whole diff after a crash and each per-user
    /// operation converges — so this is the terminal success state once a fan-out completes with no
    /// transient failure. A user skipped because their credential is dead does not block it: their
    /// catch-up is a reconciliation concern, not a reason to re-run this diff forever.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The diff is not in a dispatchable, pending state.</exception>
    public void MarkDispatched(DateTimeOffset atUtc)
    {
        if (!IsDispatchable)
        {
            throw new InvalidOperationException(
                $"A diff in {State} cannot be dispatched to calendars.");
        }

        if (CalendarDispatchState is CalendarDispatchState.Dispatched)
        {
            return;
        }

        if (CalendarDispatchState is CalendarDispatchState.Failed)
        {
            throw new InvalidOperationException(
                "A diff whose dispatch has failed terminally cannot be marked dispatched.");
        }

        CalendarDispatchState = CalendarDispatchState.Dispatched;
        DispatchedAtUtc = atUtc;
        NextAttemptAtUtc = null;
        DispatchFailureReason = null;
    }

    /// <summary>
    /// Records that a dispatch attempt failed transiently and could not fully apply the diff.
    /// </summary>
    /// <remarks>
    /// The diff stays <see cref="CalendarDispatchState.Pending"/> with a back-off time so the next
    /// cycle retries it, until <paramref name="maxAttempts"/> attempts have failed, at which point it
    /// moves to <see cref="CalendarDispatchState.Failed"/> and stops churning (ADR-059). A dead
    /// credential is not a transient failure and must not be reported here — it is handled per user
    /// without counting against the diff.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The diff is not in a dispatchable, pending state.</exception>
    public void RecordDispatchFailure(
        string reason,
        TimeSpan baseRetryDelay,
        int maxAttempts,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseRetryDelay.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        if (!IsDispatchable)
        {
            throw new InvalidOperationException(
                $"A diff in {State} cannot be dispatched to calendars.");
        }

        if (CalendarDispatchState is not CalendarDispatchState.Pending)
        {
            throw new InvalidOperationException(
                $"A dispatch failure cannot be recorded from {CalendarDispatchState}.");
        }

        DispatchAttempts++;
        DispatchFailureReason = reason.Length <= MaximumDispatchFailureReasonLength
            ? reason
            : reason[..MaximumDispatchFailureReasonLength];

        if (DispatchAttempts >= maxAttempts)
        {
            CalendarDispatchState = CalendarDispatchState.Failed;
            NextAttemptAtUtc = null;
        }
        else
        {
            // Exponential back-off from the base delay, capped so a long-lived deferral cannot
            // overflow: twelve doublings of a modest base is already days, past any transient outage.
            int exponent = Math.Min(DispatchAttempts - 1, 12);
            NextAttemptAtUtc = now + (baseRetryDelay * Math.Pow(2, exponent));
        }
    }

    /// <summary>
    /// Whether an operator may return this diff to the dispatch queue: it failed terminally, and
    /// it is still allowed to reach a calendar at all.
    /// </summary>
    public bool IsDispatchRetriable =>
        IsDispatchable && CalendarDispatchState is CalendarDispatchState.Failed;

    /// <summary>
    /// Returns a terminally failed diff to the dispatch queue on a named operator's behalf
    /// (ADR-097).
    /// </summary>
    /// <remarks>
    /// This grants no new authority. The diff is the same immutable record it always was, it must
    /// still be dispatchable, and the fan-out it re-enters is the ordinary idempotent one: an
    /// operation already applied converges through the deterministic event id and the mapping
    /// ledger rather than being applied twice. Attempts are reset because the next attempt is a
    /// fresh one; the retry itself is counted separately so resetting them hides nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The diff has not failed terminally.</exception>
    public void RetryDispatch(string retriedBy, string retryReason, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retriedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(retryReason);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            retriedBy.Length,
            MaximumDispatchRetriedByLength,
            nameof(retriedBy));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            retryReason.Length,
            MaximumDispatchRetryReasonLength,
            nameof(retryReason));

        if (!IsDispatchable)
        {
            throw new InvalidOperationException(
                $"A diff in {State} cannot be dispatched to calendars.");
        }

        if (CalendarDispatchState is not CalendarDispatchState.Failed)
        {
            throw new InvalidOperationException(
                $"Only a failed dispatch can be retried; this one is {CalendarDispatchState}.");
        }

        CalendarDispatchState = CalendarDispatchState.Pending;
        DispatchAttempts = 0;
        NextAttemptAtUtc = null;
        DispatchRetryCount++;
        LastDispatchRetriedBy = retriedBy;
        LastDispatchRetryReason = retryReason;
        LastDispatchRetriedAtUtc = atUtc;

        // DispatchFailureReason is deliberately left in place: until the retried attempt reports
        // its own outcome, why it failed last time is still the most useful thing to read.
    }

    private string? DescribeHold(ScheduleDiffSafetyThresholds thresholds)
    {
        List<string> reasons = [];

        if (AmbiguousCount > 0)
        {
            // An ambiguous entry names a record on both sides but cannot say
            // they are the same lesson. Acting on the rest of the diff while
            // ignoring these would delete the previous record of every pair.
            reasons.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} entries are ambiguous and no calendar operation may be derived from this "
                + "diff until they are resolved at the source.",
                AmbiguousCount));
        }

        if (DeletedCount >= thresholds.MinimumDeletionCount
            && PreviousRecordCount > 0
            && (double)DeletedCount / PreviousRecordCount > thresholds.MaximumDeletionShare)
        {
            reasons.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} of {1} previously published records are deleted ({2:F3}), over the "
                + "tolerated share of {3:F3}.",
                DeletedCount,
                PreviousRecordCount,
                (double)DeletedCount / PreviousRecordCount,
                thresholds.MaximumDeletionShare));
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        string reason = string.Join(" ", reasons);
        return reason.Length <= MaximumHoldReasonLength
            ? reason
            : reason[..MaximumHoldReasonLength];
    }

    private static int Count(
        IReadOnlyCollection<ScheduleDiffEntry> entries,
        ScheduleDiffChange change) =>
        entries.Count(entry => entry.Change == change);
}

/// <summary>
/// Whether a calculated diff may be acted on.
/// </summary>
public enum ScheduleDiffState
{
    /// <summary>Every entry is unambiguous and within the safety thresholds.</summary>
    Ready,

    /// <summary>Held for a human. No calendar operation may be derived from it.</summary>
    Held,

    /// <summary>
    /// Held, then released by a named operator who stated why (ADR-042). It may
    /// be dispatched, and it still carries the reason it was held.
    /// </summary>
    Released,
}

/// <summary>
/// How far a dispatchable diff has been fanned out onto student calendars (ADR-059).
/// </summary>
/// <remarks>
/// This is orthogonal to <see cref="ScheduleDiffState"/>. A diff must be
/// <see cref="ScheduleDiff.IsDispatchable"/> before dispatch begins; this then tracks whether the
/// per-user calendar operations have been applied, deferred with a back-off, or given up on.
/// </remarks>
public enum CalendarDispatchState
{
    /// <summary>Waiting to be applied, or deferred after a transient failure until its back-off passes.</summary>
    Pending,

    /// <summary>Every applicable calendar has been updated from this diff.</summary>
    Dispatched,

    /// <summary>Dispatch failed on too many attempts; it needs an operator rather than another retry.</summary>
    Failed,
}
