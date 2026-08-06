using Sirkadiyen.Domain.Scheduling.Diffing;

namespace Sirkadiyen.Application.Scheduling.Diffing;

/// <summary>
/// Calculates and stores the semantic diff of a published revision.
/// </summary>
/// <remarks>
/// Publication makes a revision live; this is what records what publishing it
/// actually changed. The two are separate transactions on purpose: a diff that
/// failed to calculate must never be able to roll back a schedule that students
/// are already entitled to see. Calculation is therefore driven by state — a
/// published revision with no diff row — so a worker killed between the two
/// steps recovers on its next cycle.
/// <para>
/// Nothing here writes to a calendar. The diff is stored, and held when it is
/// not safe to act on; dispatching it is the next stage's problem.
/// </para>
/// </remarks>
public sealed class ScheduleDiffService(
    IScheduleDiffStore store,
    SemanticScheduleDiffer differ,
    ScheduleDiffSafetyThresholds thresholds,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Calculates the diff for one published revision.
    /// </summary>
    /// <returns>
    /// The outcome, or <see langword="null"/> when the revision was not in a
    /// state that can be diffed or already had one.
    /// </returns>
    public async Task<ScheduleDiffCalculationResult?> CalculateAsync(
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        ScheduleDiffInput? input = await store.LoadAsync(revisionId, cancellationToken);
        if (input is null)
        {
            return null;
        }

        ScheduleDiff diff = ScheduleDiff.Create(
            input.ScheduleSourceId,
            input.SourceId,
            input.PreviousRevisionId,
            input.CurrentRevisionId,
            differ.Diff(input.PreviousRecords, input.CurrentRecords),
            thresholds,
            timeProvider.GetUtcNow());

        ScheduleDiffPersistenceResult persistence = await store.SaveAsync(
            diff,
            cancellationToken);

        return new ScheduleDiffCalculationResult
        {
            RevisionId = revisionId,
            Diff = diff,
            Outcome = persistence.Outcome,
        };
    }

    /// <summary>
    /// Calculates diffs for every published revision still missing one, oldest first.
    /// </summary>
    /// <remarks>
    /// One revision that cannot be diffed is reported rather than thrown, so it
    /// cannot stop the rest of the backlog.
    /// </remarks>
    public async Task<IReadOnlyList<ScheduleDiffCalculationResult>> CalculatePendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<Guid> pending = await store.ListPendingDiffAsync(limit, cancellationToken);

        List<ScheduleDiffCalculationResult> results = [];
        foreach (Guid revisionId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await CalculateAsync(revisionId, cancellationToken) is { } result)
            {
                results.Add(result);
            }
        }

        return results;
    }
}

public sealed record ScheduleDiffCalculationResult
{
    public required Guid RevisionId { get; init; }

    public required ScheduleDiff Diff { get; init; }

    public required ScheduleDiffPersistenceOutcome Outcome { get; init; }
}
