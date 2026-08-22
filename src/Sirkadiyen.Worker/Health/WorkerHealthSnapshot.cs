namespace Sirkadiyen.Worker.Health;

internal sealed record WorkerHealthSnapshot
{
    public required string Status { get; init; }
    public required string InstanceId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset LastActivityAtUtc { get; init; }
    public required string CurrentStage { get; init; }

    /// <summary>When this instance next intends to poll the schedule sources, if known (ADR-127).</summary>
    public DateTimeOffset? NextSourcePollAtUtc { get; init; }
}
