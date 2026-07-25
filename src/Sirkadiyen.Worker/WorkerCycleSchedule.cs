using Sirkadiyen.Application.ScheduleIngestion;

namespace Sirkadiyen.Worker;

internal readonly record struct WorkerCycleSchedule(
    bool PollScheduleSources,
    TimeSpan Delay);

internal static class WorkerCycleScheduler
{
    public static WorkerCycleSchedule GetNext(
        bool calendarCatchUpRequired,
        WorkerOptions options,
        AdaptivePollingIntervalPolicy intervalPolicy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(intervalPolicy);

        return calendarCatchUpRequired
            ? new WorkerCycleSchedule(
                PollScheduleSources: false,
                options.CalendarCatchUpInterval)
            : new WorkerCycleSchedule(
                PollScheduleSources: true,
                intervalPolicy.GetInterval(now));
    }
}
