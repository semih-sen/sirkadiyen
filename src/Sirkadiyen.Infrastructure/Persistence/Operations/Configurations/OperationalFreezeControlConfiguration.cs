using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Operations;

namespace Sirkadiyen.Infrastructure.Persistence.Operations.Configurations;

internal sealed class OperationalFreezeControlConfiguration
    : IEntityTypeConfiguration<OperationalFreezeControl>
{
    public void Configure(EntityTypeBuilder<OperationalFreezeControl> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("operational_freeze_control");
        builder.HasKey(control => control.Id);
        builder.Property(control => control.Id).ValueGeneratedNever();
        builder.Property(control => control.ChangedBy)
            .HasMaxLength(OperationalFreezeControl.MaximumActorLength);
        builder.Property(control => control.Reason)
            .HasMaxLength(OperationalFreezeControl.MaximumReasonLength);
        builder.Property(control => control.CorrelationId)
            .HasMaxLength(OperationalFreezeControl.MaximumCorrelationIdLength);
        builder.Property(control => control.RowVersion).IsRowVersion();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_operational_freeze_control_singleton",
            $"\"Id\" = {OperationalFreezeControl.SingletonId}"));

        // The first migration creates a known unfrozen baseline. This is not an
        // operator transition, so it deliberately has no audit row.
        builder.HasData(new
        {
            Id = OperationalFreezeControl.SingletonId,
            IsFrozen = false,
        });
    }
}
