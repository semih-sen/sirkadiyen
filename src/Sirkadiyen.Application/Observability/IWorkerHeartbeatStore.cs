namespace Sirkadiyen.Application.Observability;

/// <summary>
/// Persists and reads worker-instance heartbeats (ADR-124). Each worker process upserts its own row
/// each cycle; the administration panel reads every row to see which instances are live and what each
/// is doing. Written by the worker, read by the API — both through this one port.
/// </summary>
public interface IWorkerHeartbeatStore
{
    /// <summary>
    /// Upserts the calling instance's heartbeat and, in the same call, deletes rows for instances that
    /// have not reported since <paramref name="retentionCutoffUtc"/>, so stale rows never accumulate.
    /// </summary>
    Task RecordAsync(
        WorkerHeartbeatBeat beat,
        DateTimeOffset heartbeatAtUtc,
        DateTimeOffset retentionCutoffUtc,
        CancellationToken cancellationToken);

    /// <summary>Every retained heartbeat, newest report first.</summary>
    Task<IReadOnlyList<WorkerInstanceView>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>One instance's self-report for a single heartbeat.</summary>
public sealed record WorkerHeartbeatBeat
{
    public required string InstanceId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required string Status { get; init; }

    public required string CurrentStage { get; init; }

    public required DateTimeOffset LastActivityAtUtc { get; init; }
}

/// <summary>A read projection of one instance's last heartbeat.</summary>
public sealed record WorkerInstanceView
{
    public required string InstanceId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required string Status { get; init; }

    public required string CurrentStage { get; init; }

    public required DateTimeOffset LastActivityAtUtc { get; init; }

    public required DateTimeOffset LastHeartbeatAtUtc { get; init; }
}
