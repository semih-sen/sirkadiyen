using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Observability;

namespace Sirkadiyen.Infrastructure.Persistence.Observability.Configurations;

internal sealed class WorkerInstanceHeartbeatConfiguration
    : IEntityTypeConfiguration<WorkerInstanceHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerInstanceHeartbeat> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("worker_instances");

        // The instance id (machine:pid) is the natural key: one row per instance, upserted.
        builder.HasKey(heartbeat => heartbeat.InstanceId);
        builder.Property(heartbeat => heartbeat.InstanceId)
            .HasMaxLength(WorkerInstanceHeartbeat.MaximumInstanceIdLength)
            .ValueGeneratedNever();

        builder.Property(heartbeat => heartbeat.Status)
            .HasMaxLength(WorkerInstanceHeartbeat.MaximumStatusLength)
            .IsRequired();
        builder.Property(heartbeat => heartbeat.CurrentStage)
            .HasMaxLength(WorkerInstanceHeartbeat.MaximumStageLength)
            .IsRequired();

        // Retention prunes and the admin read both order by this, so it is indexed.
        builder.HasIndex(heartbeat => heartbeat.LastHeartbeatAtUtc);
    }
}
