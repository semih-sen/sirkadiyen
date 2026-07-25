using Sirkadiyen.Application.ScheduleIngestion;
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
        AdaptivePollingIntervalPolicy polling = CreatePollingPolicy();

        WorkerCycleSchedule result = WorkerCycleScheduler.GetNext(
            calendarCatchUpRequired: true,
            options,
            polling,
            Saturday);

        Assert.False(result.PollScheduleSources);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Delay);
    }

    [Fact]
    public void GetNext_WhenCalendarBacklogIsDrained_UsesOrdinarySourcePollingPolicy()
    {
        WorkerOptions options = CreateWorkerOptions();
        AdaptivePollingIntervalPolicy polling = CreatePollingPolicy();

        WorkerCycleSchedule result = WorkerCycleScheduler.GetNext(
            calendarCatchUpRequired: false,
            options,
            polling,
            Saturday);

        Assert.True(result.PollScheduleSources);
        Assert.Equal(TimeSpan.FromHours(1), result.Delay);
    }

    private static WorkerOptions CreateWorkerOptions() => new()
    {
        SourceCatalogPath = "schedule-sources.json",
        CalendarCatchUpInterval = TimeSpan.FromSeconds(5),
    };

    private static AdaptivePollingIntervalPolicy CreatePollingPolicy() => new(
        new AdaptivePollingOptions
        {
            TimeZoneId = "Europe/Istanbul",
            WeekendInterval = TimeSpan.FromHours(1),
        });
}
