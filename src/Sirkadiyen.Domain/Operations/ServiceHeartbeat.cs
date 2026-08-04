namespace Sirkadiyen.Domain.Operations;

/// <summary>A process-level heartbeat used to determine whether a background service is alive.</summary>
public sealed class ServiceHeartbeat
{
    public const int MaximumServiceNameLength = 100;
    public const int MaximumInstanceIdLength = 200;

    private ServiceHeartbeat() { }

    public static ServiceHeartbeat Create(
        string serviceName,
        string instanceId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset seenAtUtc) => new()
        {
            ServiceName = RequiredBounded(serviceName, MaximumServiceNameLength, nameof(serviceName)),
            InstanceId = RequiredBounded(instanceId, MaximumInstanceIdLength, nameof(instanceId)),
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = seenAtUtc,
        };

    public string ServiceName { get; private init; } = string.Empty;
    public string InstanceId { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public void Beat(string instanceId, DateTimeOffset seenAtUtc)
    {
        InstanceId = RequiredBounded(instanceId, MaximumInstanceIdLength, nameof(instanceId));
        LastSeenAtUtc = seenAtUtc;
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}
