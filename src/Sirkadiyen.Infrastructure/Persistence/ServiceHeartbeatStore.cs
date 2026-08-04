using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Operations;

namespace Sirkadiyen.Infrastructure.Persistence;

public sealed class ServiceHeartbeatStore(SirkadiyenDbContext dbContext) : IServiceHeartbeatStore
{
    public async Task RecordAsync(
        string serviceName,
        string instanceId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken)
    {
        _ = ServiceHeartbeat.Create(serviceName, instanceId, startedAtUtc, seenAtUtc);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sirkadiyen.service_heartbeats
                ("ServiceName", "InstanceId", "StartedAtUtc", "LastSeenAtUtc")
            VALUES ({serviceName.Trim()}, {instanceId.Trim()}, {startedAtUtc}, {seenAtUtc})
            ON CONFLICT ("ServiceName") DO UPDATE SET
                "InstanceId" = EXCLUDED."InstanceId",
                "StartedAtUtc" = CASE
                    WHEN sirkadiyen.service_heartbeats."InstanceId" = EXCLUDED."InstanceId"
                    THEN sirkadiyen.service_heartbeats."StartedAtUtc"
                    ELSE EXCLUDED."StartedAtUtc"
                END,
                "LastSeenAtUtc" = EXCLUDED."LastSeenAtUtc"
            """, cancellationToken);
    }

    public Task<ServiceHeartbeatSnapshot?> FindAsync(
        string serviceName,
        CancellationToken cancellationToken) =>
        dbContext.Set<ServiceHeartbeat>()
            .AsNoTracking()
            .Where(item => item.ServiceName == serviceName)
            .Select(item => new ServiceHeartbeatSnapshot
            {
                ServiceName = item.ServiceName,
                InstanceId = item.InstanceId,
                StartedAtUtc = item.StartedAtUtc,
                LastSeenAtUtc = item.LastSeenAtUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);
}
