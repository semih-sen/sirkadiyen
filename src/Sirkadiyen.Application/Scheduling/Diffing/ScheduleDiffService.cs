using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;

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
    ScheduleDiffRetryOptions retryOptions,
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
    /// cannot stop the rest of the backlog. This isolation is load-bearing: the
    /// pending list is ordered oldest-first, so without it a single revision that
    /// throws would abort the whole pass on every cycle and permanently starve the
    /// newer revisions behind it — and a revision whose diff never runs is skipped
    /// by the next one's baseline, which silently loses the deletions it carried.
    /// A caught failure is recorded on the revision, so a transient fault retries
    /// after a back-off while a persistent one stops being retried and is surfaced
    /// to the operator by name rather than lost (ADR-164).
    /// </remarks>
    public async Task<ScheduleDiffCalculationBatch> CalculatePendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<Guid> pending = await store.ListPendingDiffAsync(limit, now, cancellationToken);

        List<ScheduleDiffCalculationResult> calculated = [];
        List<ScheduleDiffCalculationFailure> failed = [];
        foreach (Guid revisionId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await CalculateAsync(revisionId, cancellationToken) is { } result)
                {
                    calculated.Add(result);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One revision that cannot be diffed must not stop the rest: record it and move on.
                // No diff row was written, so the revision is still pending in the sense that
                // matters — but recording the failure defers the next attempt, and eventually
                // stops it, so a fault that will never succeed cannot burn a cycle every six
                // seconds forever (ADR-164).
                RevisionDiffState? diffState = await store.RecordDiffCalculationFailureAsync(
                    revisionId,
                    exception.Message,
                    retryOptions.BaseRetryDelay,
                    retryOptions.MaximumAttempts,
                    now,
                    cancellationToken);

                failed.Add(new ScheduleDiffCalculationFailure
                {
                    RevisionId = revisionId,
                    Reason = exception.Message,
                    DiffState = diffState ?? RevisionDiffState.Pending,
                });
            }
        }

        return new ScheduleDiffCalculationBatch
        {
            Calculated = calculated,
            Failed = failed,
        };
    }
}

public sealed record ScheduleDiffCalculationBatch
{
    /// <summary>The revisions whose diff was calculated (or already existed) this pass.</summary>
    public required IReadOnlyList<ScheduleDiffCalculationResult> Calculated { get; init; }

    /// <summary>
    /// The revisions whose diff calculation threw and was isolated, so the rest of the pass could
    /// proceed. Each stays pending and is retried on the next cycle.
    /// </summary>
    public required IReadOnlyList<ScheduleDiffCalculationFailure> Failed { get; init; }
}

public sealed record ScheduleDiffCalculationFailure
{
    public required Guid RevisionId { get; init; }

    public required string Reason { get; init; }

    /// <summary>
    /// Whether this revision will be tried again on its own (ADR-164).
    /// <see cref="RevisionDiffState.Failed"/> means it will not: its attempts are exhausted and it
    /// now waits for an operator, which is what the alert has to say out loud.
    /// </summary>
    public required RevisionDiffState DiffState { get; init; }
}

public sealed record ScheduleDiffCalculationResult
{
    public required Guid RevisionId { get; init; }

    public required ScheduleDiff Diff { get; init; }

    public required ScheduleDiffPersistenceOutcome Outcome { get; init; }
}
