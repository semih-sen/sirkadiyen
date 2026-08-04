namespace Sirkadiyen.Worker.Scheduling;

internal readonly record struct WorkerCycleSchedule(
    bool PollScheduleSources,
    TimeSpan Delay);
