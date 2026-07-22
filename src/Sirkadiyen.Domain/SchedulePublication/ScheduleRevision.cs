using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Domain.SchedulePublication;

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

    public int RecordCount { get; private set; }

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

    public void MarkSuperseded(Guid supersededByRevisionId, DateTimeOffset atUtc)
    {
        TransitionTo(RevisionState.Superseded, atUtc);
        SupersededByRevisionId = supersededByRevisionId;
        SupersededAtUtc = atUtc;
    }

    public void SetRecordCount(int recordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
        RecordCount = recordCount;
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
