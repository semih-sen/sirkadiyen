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
