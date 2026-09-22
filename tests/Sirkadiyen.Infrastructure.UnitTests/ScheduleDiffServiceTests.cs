using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Coverage for the pending-diff pass, whose per-revision isolation is load-bearing: a revision
/// that cannot be diffed must not abort the batch, because the list is ordered oldest-first and a
/// revision that never gets a diff has the deletions it carried silently swallowed by the next
/// revision's baseline.
/// </summary>
public sealed class ScheduleDiffServiceTests
{
    private static readonly CancellationToken Token = CancellationToken.None;

    private static ScheduleDiffService Service(IScheduleDiffStore store) => new(
        store,
        new SemanticScheduleDiffer(new SemanticDiffOptions()),
        new ScheduleDiffSafetyThresholds(),
        new ScheduleDiffRetryOptions(),
        TimeProvider.System);

    [Fact]
    public async Task ARevisionThatThrowsIsIsolatedAndTheOthersStillGetDiffed()
    {
        Guid first = Guid.CreateVersion7();
        Guid poison = Guid.CreateVersion7();
        Guid last = Guid.CreateVersion7();
        FakeDiffStore store = new([first, poison, last], throwOn: poison);

        ScheduleDiffCalculationBatch batch = await Service(store).CalculatePendingAsync(10, Token);

        // The poison revision in the middle did not stop the ones behind it.
        Assert.Equal([first, last], batch.Calculated.Select(result => result.RevisionId));
        Assert.Equal([first, last], store.Saved);

        // It is reported by name, and no diff was written for it.
        ScheduleDiffCalculationFailure failure = Assert.Single(batch.Failed);
        Assert.Equal(poison, failure.RevisionId);
        Assert.Contains(poison.ToString(), failure.Reason);
        Assert.DoesNotContain(poison, store.Saved);

        // And the failure is recorded on the revision, which is what bounds the retry (ADR-164).
        // Without it the revision is pending again on the very next cycle, forever.
        (Guid revisionId, string reason, int maxAttempts) = Assert.Single(store.Failures);
        Assert.Equal(poison, revisionId);
        Assert.Contains(poison.ToString(), reason);
        Assert.Equal(new ScheduleDiffRetryOptions().MaximumAttempts, maxAttempts);
        Assert.Equal(RevisionDiffState.Pending, failure.DiffState);
    }

    [Fact]
    public async Task ARevisionThatHasExhaustedItsAttemptsIsReportedAsGivenUpOn()
    {
        // The alert has to be able to say "this one will not be tried again", because that is the
        // difference between a fault that recovers on its own and one waiting for an operator.
        Guid poison = Guid.CreateVersion7();
        FakeDiffStore store = new([poison], throwOn: poison)
        {
            FailureState = RevisionDiffState.Failed,
        };

        ScheduleDiffCalculationBatch batch = await Service(store).CalculatePendingAsync(10, Token);

        ScheduleDiffCalculationFailure failure = Assert.Single(batch.Failed);
        Assert.Equal(RevisionDiffState.Failed, failure.DiffState);
    }

    [Fact]
    public async Task AHealthyPassRecordsNoFailureAgainstAnyRevision()
    {
        FakeDiffStore store = new([Guid.CreateVersion7(), Guid.CreateVersion7()]);

        await Service(store).CalculatePendingAsync(10, Token);

        Assert.Empty(store.Failures);
    }

    [Fact]
    public async Task AllHealthyRevisionsAreCalculatedWithNoFailures()
    {
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();
        FakeDiffStore store = new([first, second]);

        ScheduleDiffCalculationBatch batch = await Service(store).CalculatePendingAsync(10, Token);

        Assert.Equal([first, second], batch.Calculated.Select(result => result.RevisionId));
        Assert.Empty(batch.Failed);
    }

    [Fact]
    public async Task CancellationStillPropagates()
    {
        Guid revision = Guid.CreateVersion7();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Service(new FakeDiffStore([revision])).CalculatePendingAsync(10, cancelled.Token));
    }

    private sealed class FakeDiffStore(IReadOnlyList<Guid> pending, Guid? throwOn = null)
        : IScheduleDiffStore
    {
        public List<Guid> Saved { get; } = [];

        /// <summary>Every failure the service recorded, in order (ADR-164).</summary>
        public List<(Guid RevisionId, string Reason, int MaxAttempts)> Failures { get; } = [];

        /// <summary>The state a recorded failure reports back, so the terminal case is testable.</summary>
        public RevisionDiffState FailureState { get; init; } = RevisionDiffState.Pending;

        public Task<IReadOnlyList<Guid>> ListPendingDiffAsync(
            int limit,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(pending);

        public Task<RevisionDiffState?> RecordDiffCalculationFailureAsync(
            Guid revisionId,
            string reason,
            TimeSpan baseRetryDelay,
            int maxAttempts,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Failures.Add((revisionId, reason, maxAttempts));
            return Task.FromResult<RevisionDiffState?>(FailureState);
        }

        public Task<RevisionDiffRetryOutcome> RetryDiffCalculationAsync(
            Guid revisionId,
            string retriedBy,
            string retryReason,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScheduleDiffInput?> LoadAsync(
            Guid revisionId,
            CancellationToken cancellationToken)
        {
            if (throwOn == revisionId)
            {
                throw new InvalidOperationException($"cannot load revision {revisionId}");
            }

            return Task.FromResult<ScheduleDiffInput?>(new ScheduleDiffInput
            {
                ScheduleSourceId = Guid.CreateVersion7(),
                SourceId = SourceId.Parse("G1-TR-ANNUAL"),
                CurrentRevisionId = revisionId,
                PreviousRevisionId = null,
                PreviousRecords = [],
                CurrentRecords = [],
            });
        }

        public Task<ScheduleDiffPersistenceResult> SaveAsync(
            ScheduleDiff diff,
            CancellationToken cancellationToken)
        {
            Saved.Add(diff.CurrentRevisionId);
            return Task.FromResult(new ScheduleDiffPersistenceResult
            {
                Outcome = ScheduleDiffPersistenceOutcome.Stored,
                ScheduleDiffId = diff.Id,
            });
        }

        public Task<IReadOnlyList<Guid>> ListPendingDispatchAsync(
            int limit,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DispatchableDiff?> LoadForDispatchAsync(
            Guid diffId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkDispatchedAsync(
            Guid diffId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CalendarDispatchState> RecordDispatchFailureAsync(
            Guid diffId,
            string reason,
            TimeSpan baseRetryDelay,
            int maxAttempts,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DispatchedDiff>> ListDispatchedForReplayAsync(
            DateTimeOffset afterDispatchedAtUtc,
            Guid afterDiffId,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
