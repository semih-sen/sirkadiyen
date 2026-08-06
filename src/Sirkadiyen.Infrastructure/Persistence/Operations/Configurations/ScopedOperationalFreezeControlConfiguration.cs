using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Operations;

namespace Sirkadiyen.Infrastructure.Persistence.Operations.Configurations;

internal sealed class ScopedOperationalFreezeControlConfiguration
    : IEntityTypeConfiguration<ScopedOperationalFreezeControl>
{
    public void Configure(EntityTypeBuilder<ScopedOperationalFreezeControl> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("scoped_operational_freeze_controls");
        builder.HasKey(control => control.Id);
        builder.Property(control => control.ProgramLanguage).HasConversion<string>();
        builder.Property(control => control.ChangedBy)
            .HasMaxLength(OperationalFreezeControl.MaximumActorLength);
        builder.Property(control => control.Reason)
            .HasMaxLength(OperationalFreezeControl.MaximumReasonLength);
        builder.Property(control => control.CorrelationId)
            .HasMaxLength(OperationalFreezeControl.MaximumCorrelationIdLength);
        builder.Property(control => control.RowVersion).IsRowVersion();
        builder.HasIndex(control => new { control.ClassYear, control.ProgramLanguage }).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_scoped_operational_freeze_class_year",
            "\"ClassYear\" BETWEEN 1 AND 6"));
    }
}
