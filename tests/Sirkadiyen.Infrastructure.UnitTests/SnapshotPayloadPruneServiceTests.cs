using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class SnapshotPayloadPruneServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid SnapshotId = Guid.CreateVersion7();

    private static readonly OperationalFreezeScope Scope = new()
    {
        ClassYear = 2,
        ProgramLanguage = ProgramLanguage.Turkish,
    };

    [Fact]
    public async Task AnEligibleSnapshotIsPruned()
    {
        FakeStore store = new(Candidate());
        SnapshotPayloadPruneService service = Service(store, frozen: false);

        SnapshotPayloadPruneResult result = await service.PruneAsync(SnapshotId, CancellationToken.None);

        Assert.Equal(SnapshotPayloadPruneOutcome.Pruned, result.Outcome);
        Assert.Equal(SnapshotId, store.PrunedSnapshotId);
        Assert.Equal(Now, store.PrunedAtUtc);
        Assert.Equal("G2-TEST", result.SourceId);
    }

    [Fact]
    public async Task AMissingSnapshotIsReportedNotFoundAndNotPruned()
    {
        FakeStore store = new(candidate: null);
        SnapshotPayloadPruneService service = Service(store, frozen: false);

        SnapshotPayloadPruneResult result = await service.PruneAsync(SnapshotId, CancellationToken.None);

        Assert.Equal(SnapshotPayloadPruneOutcome.SnapshotNotFound, result.Outcome);
        Assert.Null(store.PrunedSnapshotId);
    }

    [Fact]
    public async Task AnAlreadyPrunedPayloadIsNotPrunedAgain()
    {
        FakeStore store = new(Candidate() with { PayloadAlreadyPruned = true });
        SnapshotPayloadPruneService service = Service(store, frozen: false);

        SnapshotPayloadPruneResult result = await service.PruneAsync(SnapshotId, CancellationToken.None);

        Assert.Equal(SnapshotPayloadPruneOutcome.AlreadyPruned, result.Outcome);
        Assert.Null(store.PrunedSnapshotId);
    }

    [Fact]
    public async Task AFrozenScopeRefusesThePruneBeforeTouchingTheStore()
    {
        FakeStore store = new(Candidate());
        SnapshotPayloadPruneService service = Service(store, frozen: true);

        SnapshotPayloadPruneResult result = await service.PruneAsync(SnapshotId, CancellationToken.None);

        Assert.Equal(SnapshotPayloadPruneOutcome.Frozen, result.Outcome);
        Assert.Null(store.PrunedSnapshotId);
    }

    [Fact]
    public async Task AnIneligibleSnapshotIsRefusedWithItsReason()
    {
        FakeStore store = new(Candidate() with { IneligibleReason = "This is the newest snapshot." });
        SnapshotPayloadPruneService service = Service(store, frozen: false);

        SnapshotPayloadPruneResult result = await service.PruneAsync(SnapshotId, CancellationToken.None);

        Assert.Equal(SnapshotPayloadPruneOutcome.NotEligible, result.Outcome);
        Assert.Equal("This is the newest snapshot.", result.Detail);
        Assert.Null(store.PrunedSnapshotId);
    }

    [Fact]
    public async Task AConcurrentPruneThatWonTheRaceIsReportedAsAlreadyPruned()
    {
        FakeStore store = new(Candidate()) { PruneReturns = false };
        SnapshotPayloadPruneService service = Service(store, frozen: false);

        SnapshotPayloadPruneResult result = await service.PruneAsync(SnapshotId, CancellationToken.None);

        Assert.Equal(SnapshotPayloadPruneOutcome.AlreadyPruned, result.Outcome);
        Assert.Equal(SnapshotId, store.PrunedSnapshotId);
    }

    private static SnapshotPruneCandidate Candidate() => new()
    {
        SnapshotId = SnapshotId,
        SourceId = SourceId.Parse("G2-TEST"),
        AcquiredAtUtc = Now.AddDays(-30),
        Scope = Scope,
        PayloadAlreadyPruned = false,
        IneligibleReason = null,
    };

    private static SnapshotPayloadPruneService Service(FakeStore store, bool frozen) =>
        new(store, new FixedFreezeStore(frozen), new FixedTimeProvider(Now));

    private sealed class FakeStore(SnapshotPruneCandidate? candidate) : ISnapshotRetentionStore
    {
        public bool PruneReturns { get; init; } = true;

        public Guid? PrunedSnapshotId { get; private set; }

        public DateTimeOffset? PrunedAtUtc { get; private set; }

        public Task<SnapshotPruneCandidate?> FindPruneCandidateAsync(
            Guid snapshotId,
            CancellationToken cancellationToken) => Task.FromResult(candidate);

        public Task<bool> PrunePayloadAsync(
            Guid snapshotId,
            DateTimeOffset prunedAtUtc,
            CancellationToken cancellationToken)
        {
            PrunedSnapshotId = snapshotId;
            PrunedAtUtc = prunedAtUtc;
            return Task.FromResult(PruneReturns);
        }

        public Task<IReadOnlyList<PrunedSnapshotPayload>> PruneExpiredPayloadsAsync(
            DateTimeOffset cutoffUtc,
            DateTimeOffset prunedAtUtc,
            int batchSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedFreezeStore(bool isFrozen) : IOperationalFreezeStore
    {
        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = false });

        public Task<OperationalFreezeSnapshot> GetScopedAsync(
            OperationalFreezeScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = isFrozen, Scope = scope });

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool requestedState,
            string changedBy,
            string reason,
            string correlationId,
            DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
