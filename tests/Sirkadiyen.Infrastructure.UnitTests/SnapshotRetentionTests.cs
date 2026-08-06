using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class SnapshotRetentionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheServiceUsesTheConfiguredRecentWindow()
    {
        CapturingStore store = new();
        SnapshotRetentionService service = new(
            store,
            new FixedFreezeStore(isFrozen: false),
            new SnapshotRetentionOptions
            {
                RecentWindow = TimeSpan.FromDays(10),
                BatchSize = 37,
            },
            new FixedTimeProvider(Now));

        SnapshotRetentionResult result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(SnapshotRetentionOutcome.Completed, result.Outcome);
        Assert.Equal(Now.AddDays(-10), result.CutoffUtc);
        Assert.Equal(Now.AddDays(-10), store.CutoffUtc);
        Assert.Equal(Now, store.PrunedAtUtc);
        Assert.Equal(37, store.BatchSize);
    }

    [Fact]
    public async Task AGlobalFreezeStopsRetentionBeforeTheStore()
    {
        CapturingStore store = new();
        SnapshotRetentionService service = new(
            store,
            new FixedFreezeStore(isFrozen: true),
            new SnapshotRetentionOptions(),
            new FixedTimeProvider(Now));

        SnapshotRetentionResult result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(SnapshotRetentionOutcome.Frozen, result.Outcome);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public void PruningRemovesOnlyThePayloadAndIsIdempotent()
    {
        SourceSnapshot snapshot = Snapshot();

        snapshot.PrunePayload(Now);
        snapshot.PrunePayload(Now.AddMinutes(1));

        Assert.Null(snapshot.Payload);
        Assert.Equal(Now, snapshot.PayloadPrunedAtUtc);
        Assert.Throws<InvalidOperationException>(() => snapshot.RequirePayload());
        Assert.Equal("sha256:content", snapshot.ContentHash);
        Assert.Equal("2025-2026", snapshot.AcademicYear);
    }

    private static SourceSnapshot Snapshot() => new(
        Guid.CreateVersion7(),
        SourceId.Parse("G1-RETENTION-UNIT"),
        "snapshot-1",
        "spreadsheet-1",
        "2025-2026",
        Now.AddDays(-20),
        "sha256:content",
        "1.0",
        "{}",
        1,
        1,
        0);

    private sealed class CapturingStore : ISnapshotRetentionStore
    {
        public int CallCount { get; private set; }

        public DateTimeOffset CutoffUtc { get; private set; }

        public DateTimeOffset PrunedAtUtc { get; private set; }

        public int BatchSize { get; private set; }

        public Task<IReadOnlyList<PrunedSnapshotPayload>> PruneExpiredPayloadsAsync(
            DateTimeOffset cutoffUtc,
            DateTimeOffset prunedAtUtc,
            int batchSize,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CutoffUtc = cutoffUtc;
            PrunedAtUtc = prunedAtUtc;
            BatchSize = batchSize;
            return Task.FromResult<IReadOnlyList<PrunedSnapshotPayload>>([]);
        }
    }

    private sealed class FixedFreezeStore(bool isFrozen) : IOperationalFreezeStore
    {
        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = isFrozen });

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
