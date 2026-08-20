namespace Sirkadiyen.Domain.Observability;

/// <summary>
/// One worker process's last published heartbeat (ADR-124): which instance it is, when it started,
/// what stage it is in, and when it last reported. Persisted so the administration panel can see
/// every live instance at once — in particular, that more than one is running, which a single-URL
/// health probe cannot reveal (the condition behind the ADR-122 double-sync incident).
/// </summary>
/// <remarks>
/// There is one row per instance id (<c>machine:pid</c>), upserted each cycle. It is disposable
/// operational telemetry, not a business record: rows for instances that stopped reporting are pruned
/// on a short retention window, and nothing downstream depends on it.
/// </remarks>
public sealed class WorkerInstanceHeartbeat
{
    public const int MaximumInstanceIdLength = 200;

    public const int MaximumStageLength = 100;

    public const int MaximumStatusLength = 40;

    private WorkerInstanceHeartbeat()
    {
        // Materialization constructor.
        InstanceId = string.Empty;
        Status = string.Empty;
        CurrentStage = string.Empty;
    }

    public static WorkerInstanceHeartbeat Create(
        string instanceId,
        DateTimeOffset startedAtUtc,
        string status,
        string currentStage,
        DateTimeOffset lastActivityAtUtc,
        DateTimeOffset heartbeatAtUtc)
    {
        WorkerInstanceHeartbeat heartbeat = new()
        {
            InstanceId = Bounded(instanceId, MaximumInstanceIdLength, nameof(instanceId)),
            StartedAtUtc = Utc(startedAtUtc, nameof(startedAtUtc)),
        };
        heartbeat.Beat(status, currentStage, lastActivityAtUtc, heartbeatAtUtc);
        return heartbeat;
    }

    /// <summary>The instance identity, <c>machineName:processId</c>.</summary>
    public string InstanceId { get; private init; }

    public DateTimeOffset StartedAtUtc { get; private init; }

    public string Status { get; private set; }

    /// <summary>The pipeline stage the instance was last doing (for example <c>calendar-maintenance</c>).</summary>
    public string CurrentStage { get; private set; }

    /// <summary>When the instance last changed its in-process stage.</summary>
    public DateTimeOffset LastActivityAtUtc { get; private set; }

    /// <summary>When this row was last written; the liveness signal.</summary>
    public DateTimeOffset LastHeartbeatAtUtc { get; private set; }

    /// <summary>Updates the mutable fields of an existing row for a new heartbeat.</summary>
    public void Beat(
        string status,
        string currentStage,
        DateTimeOffset lastActivityAtUtc,
        DateTimeOffset heartbeatAtUtc)
    {
        Status = Bounded(status, MaximumStatusLength, nameof(status));
        CurrentStage = Bounded(currentStage, MaximumStageLength, nameof(currentStage));
        LastActivityAtUtc = Utc(lastActivityAtUtc, nameof(lastActivityAtUtc));
        LastHeartbeatAtUtc = Utc(heartbeatAtUtc, nameof(heartbeatAtUtc));
    }

    private static string Bounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }

    private static DateTimeOffset Utc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be expressed in UTC.", parameterName);
        }

        return value;
    }
}
