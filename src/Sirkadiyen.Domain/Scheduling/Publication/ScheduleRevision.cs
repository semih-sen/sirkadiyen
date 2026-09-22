using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Domain.Scheduling.Publication;

/// <summary>
/// A candidate version of one source's schedule, and the states it may move
/// through before it becomes live.
/// </summary>
/// <remarks>
/// Parsed output never becomes live schedule data on its own. A revision is
/// created from a parse run, validated, and only then published. Deletion in a
/// student's calendar can only follow from a published revision and a semantic
/// diff, so the state transitions here are the guard that stops a bad parse from
/// emptying calendars.
/// </remarks>
public sealed class ScheduleRevision
{
    public const int MaximumStateReasonLength = 2000;

    public const int MaximumApprovedByLength = 200;

    public const int MaximumApprovalReasonLength = 2000;

    public const int MaximumRejectedByLength = 200;

    public const int MaximumRejectionReasonLength = 2000;

    public const int MaximumDiffFailureReasonLength = 2000;

    public const int MaximumDiffRetriedByLength = 200;

    public const int MaximumDiffRetryReasonLength = 2000;

    private static readonly IReadOnlyDictionary<RevisionState, RevisionState[]> AllowedTransitions =
        new Dictionary<RevisionState, RevisionState[]>
        {
            [RevisionState.Parsed] =
                [RevisionState.Validating, RevisionState.Rejected],
            [RevisionState.Validating] =
                [RevisionState.ReviewRequired, RevisionState.Validated, RevisionState.Rejected],
            [RevisionState.ReviewRequired] =
                [RevisionState.Validated, RevisionState.Rejected],
            [RevisionState.Validated] =
                [RevisionState.Published, RevisionState.Rejected],
            [RevisionState.Published] =
                [RevisionState.Superseded],
            [RevisionState.Rejected] = [],
            [RevisionState.Superseded] = [],
        };

    private ScheduleRevision()
    {
        // Materialization constructor.
    }

    public ScheduleRevision(
        Guid scheduleSourceId,
        SourceId sourceId,
        Guid parseRunId,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.CreateVersion7();
        ScheduleSourceId = scheduleSourceId;
        SourceId = sourceId;
        ParseRunId = parseRunId;
        CreatedAtUtc = createdAtUtc;
        State = RevisionState.Parsed;
    }

    public Guid Id { get; private set; }

    public Guid ScheduleSourceId { get; private set; }

    public SourceId SourceId { get; private set; }

    public Guid ParseRunId { get; private set; }

    public RevisionState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset? SupersededAtUtc { get; private set; }

    public Guid? SupersededByRevisionId { get; private set; }

    public string? StateReason { get; private set; }

    /// <summary>Who released this revision from review, when one did.</summary>
    /// <remarks>
    /// Approval is the one way a revision validation held back can still reach
    /// students, so who did it and why they did it are part of the record rather
    /// than something reconstructed from logs. There is no identity provider yet,
    /// so the caller states the name; the field is an audit trail, not an
    /// authorization decision.
    /// </remarks>
    public string? ApprovedBy { get; private set; }

    public string? ApprovalReason { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    /// <summary>Who rejected this revision out of review, when one did (ADR-097).</summary>
    /// <remarks>
    /// Kept separate from <see cref="ApprovedBy"/> deliberately. Recording "who decided" in a
    /// field named for approval would make the audit trail state the opposite of what happened,
    /// and these two decisions are read by exactly the people who need to tell them apart.
    /// </remarks>
    public string? RejectedBy { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTimeOffset? RejectedAtUtc { get; private set; }

    public int RecordCount { get; private set; }

    /// <summary>
    /// What this revision's records say, as one value, so that the next parse can
    /// be recognised as saying the same thing before anything acts on it.
    /// </summary>
    /// <remarks>
    /// Null on revisions created before this was recorded. A null never matches,
    /// so an unrecognised predecessor simply produces a revision the way it always
    /// did, and the source recovers the shortcut on its next parse.
    /// </remarks>
    public string? RecordSetHash { get; private set; }

    public uint RowVersion { get; private set; }

    public void TransitionTo(RevisionState state, DateTimeOffset atUtc, string? reason = null)
    {
        if (!AllowedTransitions[State].Contains(state))
        {
            throw new InvalidOperationException(
                $"A schedule revision cannot move from {State} to {state}.");
        }

        State = state;
        StateReason = reason;

        if (state is RevisionState.Published)
        {
            PublishedAtUtc = atUtc;
        }
    }

    /// <summary>
    /// Releases a revision validation held for review, recording who decided so
    /// and why.
    /// </summary>
    /// <remarks>
    /// Approval only reaches <see cref="RevisionState.Validated"/>. Publication
    /// stays a separate step, so an approved revision goes through exactly the
    /// same publication transaction as one that was never held.
    /// </remarks>
    public void Approve(string approvedBy, string approvalReason, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalReason);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            approvedBy.Length,
            MaximumApprovedByLength,
            nameof(approvedBy));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            approvalReason.Length,
            MaximumApprovalReasonLength,
            nameof(approvalReason));

        if (State is not RevisionState.ReviewRequired)
        {
            throw new InvalidOperationException(
                $"Only a revision awaiting review can be approved; this one is {State}.");
        }

        // The state that held the revision is carried into the new reason, so a
        // single row still says what was overridden. The findings behind it are
        // never deleted, so the full evidence stays readable either way.
        string heldFor = StateReason ?? "an unrecorded finding";

        ApprovedBy = approvedBy;
        ApprovalReason = approvalReason;
        ApprovedAtUtc = atUtc;

        TransitionTo(
            RevisionState.Validated,
            atUtc,
            Truncate($"Approved by {approvedBy} over: {heldFor}", MaximumStateReasonLength));
    }

    /// <summary>
    /// Rejects a revision held for review, recording who decided so and why (ADR-097).
    /// </summary>
    /// <remarks>
    /// <see cref="RevisionState.Rejected"/> is terminal: a rejected revision has no transition
    /// out, so this closes the review rather than parking it somewhere else. Only a quarantined
    /// revision may be rejected — a validated one is corrected by publishing a newer revision over
    /// it, and a published one is never withdrawn at all (ADR-033).
    /// </remarks>
    public void Reject(string rejectedBy, string rejectionReason, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectionReason);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            rejectedBy.Length,
            MaximumRejectedByLength,
            nameof(rejectedBy));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            rejectionReason.Length,
            MaximumRejectionReasonLength,
            nameof(rejectionReason));

        if (State is not RevisionState.ReviewRequired)
        {
            throw new InvalidOperationException(
                $"Only a revision awaiting review can be rejected; this one is {State}.");
        }

        // The finding that held the revision is carried into the new reason, exactly as approval
        // does, so one row still says what the decision was made about. The findings themselves
        // are never deleted.
        string heldFor = StateReason ?? "an unrecorded finding";

        RejectedBy = rejectedBy;
        RejectionReason = rejectionReason;
        RejectedAtUtc = atUtc;

        TransitionTo(
            RevisionState.Rejected,
            atUtc,
            Truncate($"Rejected by {rejectedBy} over: {heldFor}", MaximumStateReasonLength));
    }

    public void MarkSuperseded(Guid supersededByRevisionId, DateTimeOffset atUtc)
    {
        TransitionTo(RevisionState.Superseded, atUtc);
        SupersededByRevisionId = supersededByRevisionId;
        SupersededAtUtc = atUtc;
    }

    /// <summary>
    /// Records how many records this revision carries and what they say.
    /// </summary>
    /// <remarks>
    /// The two are set together on purpose. A count without the set hash is a
    /// revision the next parse cannot recognise itself in, which is how a
    /// pipeline ends up publishing the same schedule over and over.
    /// </remarks>
    public void SetRecordSet(IReadOnlyCollection<CanonicalScheduleRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        RecordCount = records.Count;
        RecordSetHash = CanonicalRecordSetHash.Compute(records);
    }

    /// <summary>
    /// How diff calculation for this published revision is going (ADR-164).
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="State"/>, the same way a diff's
    /// <c>CalendarDispatchState</c> is separate from its own state (ADR-097).
    /// <see cref="RevisionState.Published"/> means students are entitled to see
    /// this revision; whether its diff has been calculated yet is a different
    /// question, read by a different consumer, and folding the two together
    /// would make a calculation failure look like an unpublication.
    /// </remarks>
    public RevisionDiffState DiffState { get; private set; }

    /// <summary>How many times diff calculation has failed for this revision.</summary>
    public int DiffAttempts { get; private set; }

    /// <summary>
    /// When the next calculation attempt is due, or <see langword="null"/> when
    /// one is due now — which is the state of every revision that has never
    /// failed, and of one an operator has just returned to the queue.
    /// </summary>
    public DateTimeOffset? NextDiffAttemptAtUtc { get; private set; }

    /// <summary>Why the last calculation attempt failed, when one has.</summary>
    public string? DiffFailureReason { get; private set; }

    /// <summary>Who returned a terminally failed calculation to the queue, and why (ADR-164).</summary>
    public string? DiffRetriedBy { get; private set; }

    public string? DiffRetryReason { get; private set; }

    public DateTimeOffset? DiffRetriedAtUtc { get; private set; }

    /// <summary>
    /// Whether an operator may return this revision to the calculation queue: its
    /// calculation failed terminally, and it is still a revision that can be
    /// diffed at all.
    /// </summary>
    public bool IsDiffCalculationRetriable =>
        DiffState is RevisionDiffState.Failed
        && State is RevisionState.Published or RevisionState.Superseded;

    /// <summary>
    /// Records that calculating this revision's diff failed, deferring the next
    /// attempt with an exponential back-off or giving up once the attempts are
    /// exhausted (ADR-164).
    /// </summary>
    /// <remarks>
    /// Before this existed, a revision whose calculation failed was pending again
    /// immediately, because "pending" is derived — a published revision with no
    /// diff row — and a failed attempt writes nothing. A permanent fault
    /// therefore produced an unbounded retry loop at the worker's cycle rate. The
    /// back-off bounds a transient fault's cost and
    /// <see cref="RevisionDiffState.Failed"/> bounds a permanent one's, without
    /// touching what makes a revision pending in the first place.
    /// </remarks>
    public void RecordDiffCalculationFailure(
        string reason,
        TimeSpan baseRetryDelay,
        int maxAttempts,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseRetryDelay.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        if (State is not (RevisionState.Published or RevisionState.Superseded))
        {
            throw new InvalidOperationException(
                $"A revision in {State} is never diffed, so it cannot fail calculation.");
        }

        if (DiffState is not RevisionDiffState.Pending)
        {
            throw new InvalidOperationException(
                $"A calculation failure cannot be recorded from {DiffState}.");
        }

        DiffAttempts++;
        DiffFailureReason = Truncate(reason, MaximumDiffFailureReasonLength);

        if (DiffAttempts >= maxAttempts)
        {
            // Terminal, and deliberately so: the revision is still published and
            // still has no diff, so it is still visible as undiffed — it has just
            // stopped consuming a cycle every six seconds to prove it.
            DiffState = RevisionDiffState.Failed;
            NextDiffAttemptAtUtc = null;
        }
        else
        {
            // Exponential back-off, capped at twelve doublings exactly as dispatch
            // retry is (ADR-097), so a deferral cannot overflow into an unreachable
            // date.
            int exponent = Math.Min(DiffAttempts - 1, 12);
            NextDiffAttemptAtUtc = now + (baseRetryDelay * Math.Pow(2, exponent));
        }
    }

    /// <summary>
    /// Returns a terminally failed calculation to the queue, recording who did so
    /// and why (ADR-164).
    /// </summary>
    /// <remarks>
    /// The attempt count is reset so the revision gets a full set of attempts
    /// again, while <see cref="DiffRetriedAtUtc"/> keeps the fact that it was
    /// retried at all — the same split dispatch retry makes, so a revision being
    /// retried repeatedly is visible as such rather than looking untouched.
    /// </remarks>
    public void RetryDiffCalculation(string retriedBy, string retryReason, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retriedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(retryReason);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            retriedBy.Length,
            MaximumDiffRetriedByLength,
            nameof(retriedBy));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            retryReason.Length,
            MaximumDiffRetryReasonLength,
            nameof(retryReason));

        if (!IsDiffCalculationRetriable)
        {
            throw new InvalidOperationException(
                $"Only a revision whose diff calculation failed terminally can be retried; "
                + $"this one is {State}/{DiffState}.");
        }

        DiffState = RevisionDiffState.Pending;
        DiffAttempts = 0;
        NextDiffAttemptAtUtc = null;
        DiffRetriedBy = retriedBy;
        DiffRetryReason = retryReason;
        DiffRetriedAtUtc = atUtc;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

public enum RevisionState
{
    Parsed,
    Validating,
    ReviewRequired,
    Validated,
    Published,
    Rejected,
    Superseded,
}

/// <summary>
/// Whether a published revision's semantic diff is still being attempted
/// (ADR-164).
/// </summary>
/// <remarks>
/// <see cref="Failed"/> does not mean the revision is unpublished or that its
/// changes were abandoned — the revision is live and its diff is still missing,
/// which is precisely what makes it worth an operator's attention. It means only
/// that automatic recalculation has stopped, so a permanent fault costs one
/// investigation instead of a worker cycle every six seconds forever.
/// </remarks>
public enum RevisionDiffState
{
    Pending,
    Failed,
}
