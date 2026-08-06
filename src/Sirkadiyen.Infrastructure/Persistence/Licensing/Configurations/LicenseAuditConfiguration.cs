using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Infrastructure.Persistence.Licensing.Configurations;

internal sealed class LicenseAuditConfiguration : IEntityTypeConfiguration<LicenseAudit>
{
    public void Configure(EntityTypeBuilder<LicenseAudit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("license_audits");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Id).ValueGeneratedNever();
        builder.Property(audit => audit.Action)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(audit => audit.ActorEmail)
            .HasMaxLength(License.MaximumActorEmailLength)
            .IsRequired();
        builder.Property(audit => audit.Reason)
            .HasMaxLength(License.MaximumReasonLength)
            .IsRequired();

        builder.HasOne<License>()
            .WithMany()
            .HasForeignKey(audit => audit.LicenseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(audit => audit.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(audit => audit.LicenseId);
        builder.HasIndex(audit => audit.OccurredAtUtc);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_license_audits_action",
            "\"Action\" IN ('Created', 'Redeemed', 'ManuallyActivated', 'Revoked', 'Expired')"));
    }
}
