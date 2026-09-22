using Sirkadiyen.Worker.Health;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class WorkerStallWatchdogPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AWorkerThatKeepsNamingStagesIsNotReported()
    {
        WorkerStallVerdict verdict = WorkerStallWatchdogPolicy.Decide(
            Snapshot(Now.AddMinutes(-2)),
            new WorkerStallWatchdogOptions(),
            Now);

        Assert.False(verdict.Report);
        Assert.False(verdict.EndWorker);
    }

    [Fact]
    public void AWorkerStuckInOneStagePastTheThresholdIsReportedButLeftRunning()
    {
        WorkerStallVerdict verdict = WorkerStallWatchdogPolicy.Decide(
            Snapshot(Now.AddHours(-3)),
            new WorkerStallWatchdogOptions(),
            Now);

        Assert.True(verdict.Report);
        Assert.Equal(TimeSpan.FromHours(3), verdict.StalledFor);

        // The default leaves the recovery to a person: ending a worker is the one thing here
        // that acts rather than reports, and it is opted into per deployment.
        Assert.False(verdict.EndWorker);
    }

    [Fact]
    public void AConfiguredRestartThresholdEndsTheWorkerOnceItIsPassed()
    {
        WorkerStallWatchdogOptions options = new()
        {
            AlertAfter = TimeSpan.FromMinutes(15),
            RestartAfter = TimeSpan.FromMinutes(45),
        };

        Assert.False(
            WorkerStallWatchdogPolicy.Decide(Snapshot(Now.AddMinutes(-30)), options, Now)
                .EndWorker);
        Assert.True(
            WorkerStallWatchdogPolicy.Decide(Snapshot(Now.AddMinutes(-46)), options, Now)
                .EndWorker);
    }

    /// <summary>
    /// Seeding the source catalog can take longer than the threshold, and no cycle is expected
    /// to advance while it does; a worker that never became ready is not a stalled one.
    /// </summary>
    [Theory]
    [InlineData("starting")]
    [InlineData("stopping")]
    public void AWorkerThatIsNotHealthyIsNeverReported(string status)
    {
        WorkerStallVerdict verdict = WorkerStallWatchdogPolicy.Decide(
            Snapshot(Now.AddHours(-5)) with { Status = status },
            new WorkerStallWatchdogOptions(),
            Now);

        Assert.False(verdict.Report);
        Assert.False(verdict.EndWorker);
    }

    [Fact]
    public void ARestartThresholdThatWouldFireBeforeTheReportIsRefused()
    {
        WorkerStallWatchdogOptions options = new()
        {
            AlertAfter = TimeSpan.FromMinutes(30),
            RestartAfter = TimeSpan.FromMinutes(10),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static WorkerHealthSnapshot Snapshot(DateTimeOffset lastActivityAtUtc) => new()
    {
        Status = "healthy",
        InstanceId = "ubuntu:1",
        StartedAtUtc = Now.AddHours(-6),
        LastActivityAtUtc = lastActivityAtUtc,
        CurrentStage = "calendar-inventory",
    };
}
