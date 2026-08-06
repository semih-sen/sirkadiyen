using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Configurations;

internal sealed class FinanceDistributionConfiguration : IEntityTypeConfiguration<FinanceDistribution>
{
    public void Configure(EntityTypeBuilder<FinanceDistribution> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_distributions");
        builder.HasKey(distribution => distribution.Id);
        builder.Property(distribution => distribution.Id).ValueGeneratedNever();
        builder.Property(distribution => distribution.DistributableAmount).HasPrecision(18, 2);
        builder.Property(distribution => distribution.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(distribution => distribution.PlanHash)
            .HasMaxLength(FinanceDistribution.PlanHashLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(distribution => distribution.Reason)
            .HasMaxLength(FinanceDistribution.MaximumReasonLength)
            .IsRequired();
        builder.Property(distribution => distribution.ExecutedByEmail)
            .HasMaxLength(FinanceDistribution.MaximumActorEmailLength)
            .IsRequired();
        builder.Property(distribution => distribution.ReversedByEmail)
            .HasMaxLength(FinanceDistribution.MaximumActorEmailLength);
        builder.Property(distribution => distribution.ReversalReason)
            .HasMaxLength(FinanceDistribution.MaximumReasonLength);
        builder.Property(distribution => distribution.RowVersion).IsRowVersion();

        builder.HasOne<FinanceAccount>()
            .WithMany()
            .HasForeignKey(distribution => distribution.SourceFinanceAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(distribution => distribution.ExecutedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(distribution => distribution.ReversedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Non-repeatability and idempotency, enforced by the schema rather than application logic
        // alone.
        builder.HasIndex(distribution => new { distribution.PeriodStartOn, distribution.PeriodEndOn })
            .IsUnique()
            .HasFilter("\"Status\" = 'Executed'");
        builder.HasIndex(distribution => distribution.ConfirmationToken).IsUnique();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_distributions_status",
            "\"Status\" IN ('Executed', 'Reversed')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_distributions_amount",
            "\"DistributableAmount\" > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_distributions_period",
            "\"PeriodEndOn\" >= \"PeriodStartOn\""));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_distributions_reversal",
            """
            ("Status" = 'Reversed'
             AND "ReversedByUserId" IS NOT NULL
             AND "ReversedByEmail" IS NOT NULL
             AND "ReversalReason" IS NOT NULL
             AND "ReversedAtUtc" IS NOT NULL)
            OR
            ("Status" <> 'Reversed'
             AND "ReversedByUserId" IS NULL
             AND "ReversedByEmail" IS NULL
             AND "ReversalReason" IS NULL
             AND "ReversedAtUtc" IS NULL)
            """));
    }
}
