namespace Sirkadiyen.Worker.Health;

/// <summary>What the watchdog should do about the worker's current activity timestamp.</summary>
internal readonly record struct WorkerStallVerdict(
    bool Report,
    bool EndWorker,
    TimeSpan StalledFor);

/// <summary>
/// Decides, from a health snapshot alone, whether the cycle loop has stopped advancing.
/// </summary>
/// <remarks>
/// Kept pure and separate from the hosted service that acts on it, as the cycle scheduler is:
/// the thresholds are the part worth stating exactly and reading back in a test, while the
/// acting part is a log line, an alert and a shutdown request.
/// </remarks>
internal static class WorkerStallWatchdogPolicy
{
    public static WorkerStallVerdict Decide(
        WorkerHealthSnapshot snapshot,
        WorkerStallWatchdogOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        // Starting and stopping are both states in which no cycle is expected to advance.
        if (!string.Equals(snapshot.Status, "healthy", StringComparison.Ordinal))
        {
            return new WorkerStallVerdict(false, false, TimeSpan.Zero);
        }

        TimeSpan stalled = now - snapshot.LastActivityAtUtc;
        if (stalled < options.AlertAfter)
        {
            return new WorkerStallVerdict(false, false, stalled);
        }

        bool endWorker = options.RestartAfter != TimeSpan.Zero
            && stalled >= options.RestartAfter;
        return new WorkerStallVerdict(true, endWorker, stalled);
    }
}
