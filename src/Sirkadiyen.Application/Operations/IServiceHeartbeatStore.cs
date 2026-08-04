namespace Sirkadiyen.Application.Operations;

public interface IServiceHeartbeatStore
{
    Task RecordAsync(
        string serviceName,
        string instanceId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken);

    Task<ServiceHeartbeatSnapshot?> FindAsync(
        string serviceName,
        CancellationToken cancellationToken);
}

public sealed record ServiceHeartbeatSnapshot
{
    public required string ServiceName { get; init; }
    public required string InstanceId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset LastSeenAtUtc { get; init; }
}
