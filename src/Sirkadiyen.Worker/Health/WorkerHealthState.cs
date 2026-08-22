namespace Sirkadiyen.Worker.Health;

/// <summary>In-process health state exposed only by the Worker's internal HTTP listener.</summary>
internal sealed class WorkerHealthState(TimeProvider timeProvider)
{
    private readonly object gate = new();
    private readonly DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();
    private readonly string instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
    private DateTimeOffset lastActivityAtUtc = timeProvider.GetUtcNow();
    private string currentStage = "starting";
    private bool ready;
    private DateTimeOffset? nextSourcePollAtUtc;

    public void MarkReady(string stage) => Update(stage, isReady: true);

    public void MarkActivity(string stage) => Update(stage, isReady: null);

    public void MarkStopped() => Update("stopping", isReady: false);

    /// <summary>
    /// Records when this instance next intends to poll the schedule sources, so the admin monitor
    /// can show a countdown to the next cycle (ADR-127).
    /// </summary>
    public void SetNextSourcePollAt(DateTimeOffset nextPollAtUtc)
    {
        lock (gate)
        {
            nextSourcePollAtUtc = nextPollAtUtc;
        }
    }

    public WorkerHealthSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new WorkerHealthSnapshot
            {
                Status = ready ? "healthy" : "starting",
                InstanceId = instanceId,
                StartedAtUtc = startedAtUtc,
                LastActivityAtUtc = lastActivityAtUtc,
                CurrentStage = currentStage,
                NextSourcePollAtUtc = nextSourcePollAtUtc,
            };
        }
    }

    private void Update(string stage, bool? isReady)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        lock (gate)
        {
            currentStage = stage;
            lastActivityAtUtc = timeProvider.GetUtcNow();
            if (isReady is not null)
            {
                ready = isReady.Value;
            }
        }
    }
}
