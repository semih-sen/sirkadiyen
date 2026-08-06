using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Operations;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class OperationalFreezeAuditConfiguration
    : IEntityTypeConfiguration<OperationalFreezeAudit>
{
    public void Configure(EntityTypeBuilder<OperationalFreezeAudit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("operational_freeze_audits");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.ChangedBy)
            .HasMaxLength(OperationalFreezeControl.MaximumActorLength)
            .IsRequired();
        builder.Property(audit => audit.Reason)
            .HasMaxLength(OperationalFreezeControl.MaximumReasonLength)
            .IsRequired();
        builder.Property(audit => audit.CorrelationId)
            .HasMaxLength(OperationalFreezeControl.MaximumCorrelationIdLength)
            .IsRequired();

        builder.HasOne<OperationalFreezeControl>()
            .WithMany()
            .HasForeignKey(audit => audit.OperationalFreezeControlId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(audit => audit.ChangedAtUtc);
    }
}
