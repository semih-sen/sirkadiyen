namespace Sirkadiyen.Application.Administration;

public interface IAdminServiceHealthProbe
{
    Task<AdminServiceHealthSnapshot> GetAsync(CancellationToken cancellationToken);
}

public sealed record AdminServiceHealthSnapshot
{
    public required DateTimeOffset CheckedAtUtc { get; init; }
    public required ServiceHealthView Worker { get; init; }
    public required ServiceHealthView Parser { get; init; }
}

public sealed record ServiceHealthView
{
    public required string Service { get; init; }
    public required ServiceHealthState State { get; init; }
    public DateTimeOffset? LastSeenAtUtc { get; init; }
    public string? Detail { get; init; }
}

public enum ServiceHealthState
{
    Healthy,
    Unhealthy,
    Unknown,
}
