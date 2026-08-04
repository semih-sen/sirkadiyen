using Sirkadiyen.Worker.Configuration;

namespace Sirkadiyen.Worker.Scheduling;

internal static class WorkerCycleScheduler
{
    public static WorkerCycleSchedule GetNext(
        bool calendarCatchUpRequired,
        DateTimeOffset nextSourcePollAt,
        WorkerOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(options);

        TimeSpan calendarDelay = calendarCatchUpRequired
            ? options.CalendarCatchUpInterval
            : options.CalendarIdleCheckInterval;
        TimeSpan sourceDelay = nextSourcePollAt - now;

        if (sourceDelay <= TimeSpan.Zero)
        {
            return new WorkerCycleSchedule(PollScheduleSources: true, TimeSpan.Zero);
        }

        return sourceDelay <= calendarDelay
            ? new WorkerCycleSchedule(PollScheduleSources: true, sourceDelay)
            : new WorkerCycleSchedule(PollScheduleSources: false, calendarDelay);
    }
}
