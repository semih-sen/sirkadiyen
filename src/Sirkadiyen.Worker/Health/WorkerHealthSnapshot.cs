namespace Sirkadiyen.Worker.Health;

internal sealed record WorkerHealthSnapshot
{
    public required string Status { get; init; }
    public required string InstanceId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset LastActivityAtUtc { get; init; }
    public required string CurrentStage { get; init; }
}
