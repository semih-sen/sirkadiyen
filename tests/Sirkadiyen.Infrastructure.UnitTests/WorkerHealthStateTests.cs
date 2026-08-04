using Sirkadiyen.Worker;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class WorkerHealthStateTests
{
    [Fact]
    public void BecomesReadyAndTracksCurrentPipelineStageWithoutDatabaseState()
    {
        DateTimeOffset startedAtUtc = new(2026, 8, 4, 18, 0, 0, TimeSpan.Zero);
        MutableTimeProvider timeProvider = new(startedAtUtc);
        WorkerHealthState state = new(timeProvider);

        Assert.Equal("starting", state.GetSnapshot().Status);

        timeProvider.UtcNow = startedAtUtc.AddSeconds(1);
        state.MarkReady("ready");
        timeProvider.UtcNow = startedAtUtc.AddSeconds(5);
        state.MarkActivity("polling-sources");

        WorkerHealthSnapshot snapshot = state.GetSnapshot();
        Assert.Equal("healthy", snapshot.Status);
        Assert.Equal("polling-sources", snapshot.CurrentStage);
        Assert.Equal(startedAtUtc, snapshot.StartedAtUtc);
        Assert.Equal(startedAtUtc.AddSeconds(5), snapshot.LastActivityAtUtc);
        Assert.NotEmpty(snapshot.InstanceId);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
