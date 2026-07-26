using Sirkadiyen.Worker;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class WorkerCycleSchedulerTests
{
    private static readonly DateTimeOffset Saturday =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetNext_WhenCalendarWorkYielded_UsesShortCatchUpWithoutSourcePolling()
    {
        WorkerOptions options = CreateWorkerOptions();

        WorkerCycleSchedule result = WorkerCycleScheduler.GetNext(
            calendarCatchUpRequired: true,
            nextSourcePollAt: Saturday.AddHours(1),
            options,
            Saturday);

        Assert.False(result.PollScheduleSources);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Delay);
    }

    [Fact]
    public void GetNext_WhenCalendarBacklogIsDrained_ChecksForNewCalendarWorkPromptly()
    {
        WorkerOptions options = CreateWorkerOptions();

        WorkerCycleSchedule result = WorkerCycleScheduler.GetNext(
            calendarCatchUpRequired: false,
            nextSourcePollAt: Saturday.AddHours(1),
            options,
            Saturday);

        Assert.False(result.PollScheduleSources);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Delay);
    }

    [Fact]
    public void GetNext_WhenSourcePollIsDueBeforeIdleCheck_PreservesSourceDeadline()
    {
        WorkerOptions options = CreateWorkerOptions();

        WorkerCycleSchedule result = WorkerCycleScheduler.GetNext(
            calendarCatchUpRequired: false,
            nextSourcePollAt: Saturday.AddSeconds(3),
            options,
            Saturday);

        Assert.True(result.PollScheduleSources);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Delay);
    }

    [Fact]
    public void GetNext_WhenSourcePollIsOverdue_StartsItWithoutDelay()
    {
        WorkerOptions options = CreateWorkerOptions();

        WorkerCycleSchedule result = WorkerCycleScheduler.GetNext(
            calendarCatchUpRequired: false,
            nextSourcePollAt: Saturday.AddSeconds(-1),
            options,
            Saturday);

        Assert.True(result.PollScheduleSources);
        Assert.Equal(TimeSpan.Zero, result.Delay);
    }

    [Fact]
    public void Validate_WhenCalendarIdleCheckIsNotPositive_RejectsConfiguration()
    {
        WorkerOptions options = CreateWorkerOptions() with
        {
            CalendarIdleCheckInterval = TimeSpan.Zero,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains("idle-check interval", exception.Message, StringComparison.Ordinal);
    }

    private static WorkerOptions CreateWorkerOptions() => new()
    {
        SourceCatalogPath = "schedule-sources.json",
        CalendarCatchUpInterval = TimeSpan.FromSeconds(5),
        CalendarIdleCheckInterval = TimeSpan.FromSeconds(5),
    };
}
