using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Configurations;

internal sealed class FinanceSettlementConfiguration : IEntityTypeConfiguration<FinanceSettlement>
{
    public void Configure(EntityTypeBuilder<FinanceSettlement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_settlements");
        builder.HasKey(settlement => settlement.Id);
        builder.Property(settlement => settlement.Id).ValueGeneratedNever();
        builder.Property(settlement => settlement.Direction)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(settlement => settlement.Amount).HasPrecision(18, 2);

        builder.HasOne<FinanceObligation>()
            .WithMany()
            .HasForeignKey(settlement => settlement.FinanceObligationId)
            .OnDelete(DeleteBehavior.Restrict);
        // Restrict is what makes "a settlement-linked transaction refuses edit and delete"
        // schema-enforced rather than merely coded (ADR-093).
        builder.HasOne<FinanceTransaction>()
            .WithMany()
            .HasForeignKey(settlement => settlement.FinanceTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(settlement => new { settlement.FinanceObligationId, settlement.FinanceTransactionId })
            .IsUnique();
        builder.HasIndex(settlement => new { settlement.Direction, settlement.SettledOn });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_settlements_direction",
            "\"Direction\" IN ('Receivable', 'Payable')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_settlements_amount",
            "\"Amount\" > 0"));
    }
}
