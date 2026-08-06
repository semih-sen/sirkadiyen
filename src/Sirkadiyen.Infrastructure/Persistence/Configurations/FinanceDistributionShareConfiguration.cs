using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class FinanceDistributionShareConfiguration : IEntityTypeConfiguration<FinanceDistributionShare>
{
    public void Configure(EntityTypeBuilder<FinanceDistributionShare> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("profit_distribution_shares");
        builder.HasKey(share => share.Id);
        builder.Property(share => share.Id).ValueGeneratedNever();
        builder.Property(share => share.AllocatedAmount).HasPrecision(18, 2);

        builder.HasOne<FinanceDistribution>()
            .WithMany()
            .HasForeignKey(share => share.FinanceDistributionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinanceAccountHolder>()
            .WithMany()
            .HasForeignKey(share => share.FinanceAccountHolderId)
            .OnDelete(DeleteBehavior.Restrict);
        // Restrict is what makes "a distribution payout transaction refuses edit and delete"
        // schema-enforced (ADR-093).
        builder.HasOne<FinanceTransaction>()
            .WithMany()
            .HasForeignKey(share => share.FinanceTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(share => new { share.FinanceDistributionId, share.FinanceAccountHolderId })
            .IsUnique();
        builder.HasIndex(share => share.FinanceTransactionId).IsUnique();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_profit_distribution_shares_amount",
            "\"AllocatedAmount\" > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_profit_distribution_shares_basis_points",
            $"\"ShareBasisPoints\" BETWEEN 1 AND {FinanceAccountHolder.MaximumShareBasisPoints}"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_profit_distribution_shares_exact",
            "\"ExactShareMinorUnits\" >= 0"));
    }
}
