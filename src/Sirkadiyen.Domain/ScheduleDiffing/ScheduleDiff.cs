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
/// <see cref="ScheduleDiffState.Held"/> and never mutates afterwards. Correcting
/// a bad publication is forward-fix only (ADR-033): the next revision produces
/// the next diff.
/// </para>
/// </remarks>
public sealed class ScheduleDiff
{
    public const int MaximumHoldReasonLength = 2000;

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

    public IReadOnlyList<ScheduleDiffEntry> Entries => entries;

    /// <summary>
    /// Whether a consumer may turn this diff into calendar operations.
    /// </summary>
    public bool IsDispatchable => State is ScheduleDiffState.Ready;

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
}
