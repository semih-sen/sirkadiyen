using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Observability;

namespace Sirkadiyen.Api.Administration;

public static class ServiceHealthEndpoints
{
    /// <summary>
    /// How long since a worker instance's last heartbeat before it is considered no longer running
    /// (ADR-124). Comfortably larger than a normal cycle so a busy instance is not flagged as gone.
    /// </summary>
    private const int ActiveThresholdSeconds = 150;

    public static IEndpointRouteBuilder MapServiceHealthEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.MapGet(
                "/api/admin/services/health",
                (IAdminServiceHealthProbe probe, CancellationToken cancellationToken) =>
                    probe.GetAsync(cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Observability")
            .WithSummary("Probes the internal worker and parser health endpoints.");

        builder.MapGet("/api/admin/workers", ListWorkersAsync)
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Observability")
            .WithSummary("Lists every worker instance's last heartbeat, so concurrent instances show.");

        return builder;
    }

    private static async Task<IResult> ListWorkersAsync(
        IWorkerHeartbeatStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        TimeSpan activeThreshold = TimeSpan.FromSeconds(ActiveThresholdSeconds);

        IReadOnlyList<WorkerInstanceView> instances = await store.ListAsync(cancellationToken);

        List<WorkerInstanceStatus> statuses = [.. instances.Select(instance => new WorkerInstanceStatus
        {
            InstanceId = instance.InstanceId,
            Status = instance.Status,
            CurrentStage = instance.CurrentStage,
            StartedAtUtc = instance.StartedAtUtc,
            LastActivityAtUtc = instance.LastActivityAtUtc,
            LastHeartbeatAtUtc = instance.LastHeartbeatAtUtc,
            NextSourcePollAtUtc = instance.NextSourcePollAtUtc,
            IsActive = now - instance.LastHeartbeatAtUtc <= activeThreshold,
        })];

        return Results.Ok(new WorkerInstancesResponse
        {
            CheckedAtUtc = now,
            ActiveThresholdSeconds = ActiveThresholdSeconds,
            ActiveInstanceCount = statuses.Count(status => status.IsActive),
            Instances = statuses,
        });
    }
}

public sealed record WorkerInstancesResponse
{
    public required DateTimeOffset CheckedAtUtc { get; init; }

    public required int ActiveThresholdSeconds { get; init; }

    /// <summary>Instances whose last heartbeat is within the active window. More than one is a warning.</summary>
    public required int ActiveInstanceCount { get; init; }

    public required IReadOnlyList<WorkerInstanceStatus> Instances { get; init; }
}

public sealed record WorkerInstanceStatus
{
    public required string InstanceId { get; init; }

    public required string Status { get; init; }

    public required string CurrentStage { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset LastActivityAtUtc { get; init; }

    public required DateTimeOffset LastHeartbeatAtUtc { get; init; }

    /// <summary>When this instance next intends to poll the schedule sources, if known (ADR-127).</summary>
    public DateTimeOffset? NextSourcePollAtUtc { get; init; }

    public required bool IsActive { get; init; }
}
