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

        // It is reported by name, and left pending: no diff was written for it.
        ScheduleDiffCalculationFailure failure = Assert.Single(batch.Failed);
        Assert.Equal(poison, failure.RevisionId);
        Assert.Contains(poison.ToString(), failure.Reason);
        Assert.DoesNotContain(poison, store.Saved);
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

        public Task<IReadOnlyList<Guid>> ListPendingDiffAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(pending);

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
