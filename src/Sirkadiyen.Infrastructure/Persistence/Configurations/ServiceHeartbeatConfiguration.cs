using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Operations;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class ServiceHeartbeatConfiguration : IEntityTypeConfiguration<ServiceHeartbeat>
{
    public void Configure(EntityTypeBuilder<ServiceHeartbeat> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("service_heartbeats");
        builder.HasKey(heartbeat => heartbeat.ServiceName);
        builder.Property(heartbeat => heartbeat.ServiceName)
            .HasMaxLength(ServiceHeartbeat.MaximumServiceNameLength)
            .ValueGeneratedNever();
        builder.Property(heartbeat => heartbeat.InstanceId)
            .HasMaxLength(ServiceHeartbeat.MaximumInstanceIdLength)
            .IsRequired();
    }
}
