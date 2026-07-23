using Sirkadiyen.Domain.ScheduleParsing;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The staleness boundary of a parse run (ADR-050).
/// </summary>
/// <remarks>
/// A run is keyed by snapshot and parser profile version. One left running by a
/// killed worker therefore blocks that snapshot permanently, and the schedule
/// change it carries never reaches a calendar until the source happens to change
/// again. These tests pin the boundary itself, separately from the persistence
/// path that uses it.
/// </remarks>
public sealed class ParseRunRecoveryTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    [Fact]
    public void ARunIsNotStaleBeforeItsTimeoutElapses()
    {
        ParseRun run = Run();

        Assert.False(run.IsStale(StartedAt, StaleAfter));
        Assert.False(run.IsStale(StartedAt.Add(StaleAfter) - TimeSpan.FromTicks(1), StaleAfter));
    }

    [Fact]
    public void ARunIsStaleOnceItsTimeoutElapses()
    {
        ParseRun run = Run();

        Assert.True(run.IsStale(StartedAt.Add(StaleAfter), StaleAfter));
    }

    /// <summary>
    /// A finished run is never stale, however long ago it finished. Only an open
    /// run can be abandoned.
    /// </summary>
    [Theory]
    [InlineData(ParseRunStatus.Completed)]
    [InlineData(ParseRunStatus.CompletedWithWarnings)]
    [InlineData(ParseRunStatus.Rejected)]
    public void AFinishedRunIsNeverStale(ParseRunStatus status)
    {
        ParseRun run = Run();
        run.Complete(status, StartedAt.AddSeconds(5), "{}", 1, 0, 0);

        Assert.False(run.IsStale(StartedAt.AddYears(1), StaleAfter));
    }

    [Fact]
    public void AFailedRunIsNotStaleBecauseResumingItIsAlreadyTheNormalPath()
    {
        ParseRun run = Run();
        run.Fail(StartedAt.AddSeconds(5), "HTTP timeout");

        Assert.False(run.IsStale(StartedAt.AddYears(1), StaleAfter));
    }

    [Fact]
    public void RecoveringAStaleRunReopensItAndRecordsThat()
    {
        ParseRun run = Run();
        DateTimeOffset recoveredAt = StartedAt.Add(StaleAfter);

        run.RecoverStale("correlation-2", recoveredAt, StaleAfter);

        Assert.Equal(ParseRunStatus.Running, run.Status);
        Assert.Equal("correlation-2", run.CorrelationId);
        Assert.Equal(recoveredAt, run.StartedAtUtc);
        Assert.Equal(recoveredAt, run.LastStaleRecoveryAtUtc);
        Assert.Equal(2, run.AttemptCount);
        Assert.Null(run.CompletedAtUtc);
        Assert.Null(run.Response);
    }

    [Fact]
    public void ARunStillWithinItsTimeoutCannotBeRecovered()
    {
        ParseRun run = Run();

        Assert.Throws<InvalidOperationException>(
            () => run.RecoverStale("correlation-2", StartedAt.AddMinutes(1), StaleAfter));
        Assert.Equal(1, run.AttemptCount);
        Assert.Null(run.LastStaleRecoveryAtUtc);
    }

    /// <summary>
    /// Recovery survives a resume, because it says something about the host that
    /// a later ordinary retry does not erase.
    /// </summary>
    [Fact]
    public void ARecoveryRecordSurvivesALaterFailureAndResume()
    {
        ParseRun run = Run();
        DateTimeOffset recoveredAt = StartedAt.Add(StaleAfter);
        run.RecoverStale("correlation-2", recoveredAt, StaleAfter);

        run.Fail(recoveredAt.AddSeconds(5), "HTTP timeout");
        run.Resume("correlation-3", recoveredAt.AddMinutes(1));

        Assert.Equal(recoveredAt, run.LastStaleRecoveryAtUtc);
        Assert.Equal(3, run.AttemptCount);
        Assert.Null(run.FailureReason);
    }

    [Fact]
    public void AnImpossibleTimeoutIsRefusedRatherThanTreatedAsZero()
    {
        ParseRun run = Run();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => run.IsStale(StartedAt, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => run.IsStale(StartedAt, TimeSpan.FromMinutes(-1)));
    }

    private static ParseRun Run() => new(
        Guid.CreateVersion7(),
        "grade1_yearly_v1",
        "1.0.0",
        "correlation-1",
        StartedAt);
}
