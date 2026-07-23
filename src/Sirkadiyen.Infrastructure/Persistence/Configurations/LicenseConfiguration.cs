using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("licenses");
        builder.HasKey(license => license.Id);
        builder.Property(license => license.Id).ValueGeneratedNever();
        builder.Property(license => license.CodeHash);
        builder.Property(license => license.Kind)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(license => license.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(license => license.CreatedByEmail)
            .HasMaxLength(License.MaximumActorEmailLength)
            .IsRequired();
        builder.Property(license => license.Notes)
            .HasMaxLength(License.MaximumNotesLength);
        builder.Property(license => license.RevokedByEmail)
            .HasMaxLength(License.MaximumActorEmailLength);
        builder.Property(license => license.RevocationReason)
            .HasMaxLength(License.MaximumReasonLength);
        builder.Property(license => license.RowVersion).IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(license => license.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(license => license.RedeemedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(license => license.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(license => license.CodeHash).IsUnique();
        builder.HasIndex(license => license.Status);
        builder.HasIndex(license => license.ExpiresAtUtc);
        builder.HasIndex(license => license.RedeemedByUserId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Redeemed'");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_licenses_status",
            "\"Status\" IN ('Active', 'Redeemed', 'Revoked', 'Expired')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_licenses_kind",
            "\"Kind\" IN ('Code', 'Manual')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_licenses_code_hash",
            $"""
            ("Kind" = 'Code' AND octet_length("CodeHash") = {License.CodeHashLength})
            OR ("Kind" = 'Manual' AND "CodeHash" IS NULL)
            """));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_licenses_expiration",
            "\"ExpiresAtUtc\" IS NULL OR \"ExpiresAtUtc\" > \"CreatedAtUtc\""));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_licenses_redemption",
            """
            ("RedeemedByUserId" IS NULL) = ("RedeemedAtUtc" IS NULL)
            AND ("Status" <> 'Redeemed'
                 OR ("RedeemedByUserId" IS NOT NULL AND "RedeemedAtUtc" IS NOT NULL))
            AND ("Status" NOT IN ('Active', 'Expired')
                 OR "RedeemedByUserId" IS NULL)
            """));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_licenses_revocation",
            """
            ("Status" = 'Revoked'
             AND "RevokedByUserId" IS NOT NULL
             AND "RevokedByEmail" IS NOT NULL
             AND "RevocationReason" IS NOT NULL
             AND "RevokedAtUtc" IS NOT NULL)
            OR
            ("Status" <> 'Revoked'
             AND "RevokedByUserId" IS NULL
             AND "RevokedByEmail" IS NULL
             AND "RevocationReason" IS NULL
             AND "RevokedAtUtc" IS NULL)
            """));
    }
}

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
