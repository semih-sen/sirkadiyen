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

    public void MarkReady(string stage) => Update(stage, isReady: true);

    public void MarkActivity(string stage) => Update(stage, isReady: null);

    public void MarkStopped() => Update("stopping", isReady: false);

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
