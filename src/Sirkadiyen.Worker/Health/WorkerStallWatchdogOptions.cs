namespace Sirkadiyen.Worker.Health;

/// <summary>
/// When a worker that has stopped advancing is reported, and when it is ended so the service
/// manager can start a fresh one.
/// </summary>
/// <remarks>
/// The cycle loop names the stage it is entering before each one, so a worker that is working
/// restamps its activity continuously. An activity timestamp that stops moving therefore means
/// the loop itself stopped moving — not that the work is large.
/// </remarks>
internal sealed record WorkerStallWatchdogOptions
{
    /// <summary>How often the watchdog looks at the worker's own activity timestamp.</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long one stage may hold the loop before the operator is told. Generous on purpose:
    /// this is meant to catch a loop that has stopped, not a sweep that is merely busy.
    /// </summary>
    public TimeSpan AlertAfter { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long one stage may hold the loop before this worker ends itself, or
    /// <see cref="TimeSpan.Zero"/> to never end it.
    /// </summary>
    /// <remarks>
    /// Off by default. Ending the process is the one thing here that acts rather than reports,
    /// and every calendar stage is written to resume from what is durably recorded, so a restart
    /// costs the work in flight and nothing else. Turn it on once a deployment's ordinary stage
    /// durations are known, and set it well above the longest of them.
    /// </remarks>
    public TimeSpan RestartAfter { get; init; } = TimeSpan.Zero;

    public void Validate()
    {
        if (CheckInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The stall watchdog check interval must be positive.");
        }

        if (AlertAfter <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The stall watchdog alert threshold must be positive.");
        }

        if (RestartAfter != TimeSpan.Zero && RestartAfter <= AlertAfter)
        {
            throw new InvalidOperationException(
                "The stall watchdog restart threshold must be later than the alert threshold, "
                + "so a stall is always reported before a worker is ended.");
        }
    }
}
