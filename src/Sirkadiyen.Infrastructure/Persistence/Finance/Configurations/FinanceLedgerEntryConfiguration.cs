using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class FinanceLedgerEntryConfiguration : IEntityTypeConfiguration<FinanceLedgerEntry>
{
    public void Configure(EntityTypeBuilder<FinanceLedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_ledger_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Kind)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(entry => entry.Leg)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(entry => entry.Amount).HasPrecision(18, 2);

        builder.HasOne<FinanceTransaction>()
            .WithMany()
            .HasForeignKey(entry => entry.FinanceTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinanceAccount>()
            .WithMany()
            .HasForeignKey(entry => entry.FinanceAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new { entry.FinanceTransactionId, entry.Leg }).IsUnique();
        builder.HasIndex(entry => new { entry.FinanceTransactionId, entry.FinanceAccountId }).IsUnique();
        builder.HasIndex(entry => new { entry.FinanceAccountId, entry.Kind })
            .IsUnique()
            .HasFilter("\"Kind\" = 'OpeningBalance'");
        builder.HasIndex(entry => new { entry.FinanceAccountId, entry.OccurredOn })
            .IncludeProperties(entry => entry.Amount);
        builder.HasIndex(entry => new { entry.OccurredOn, entry.Kind });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_ledger_entries_kind",
            "\"Kind\" IN ('OpeningBalance', 'Income', 'Expense', 'Transfer', 'Distribution')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_ledger_entries_amount",
            "\"Amount\" <> 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_ledger_entries_leg",
            """
            ("Kind" = 'Transfer' AND "Leg" IN ('From', 'To') AND (("Leg" = 'From') = ("Amount" < 0)))
            OR ("Kind" IN ('OpeningBalance', 'Income', 'Expense', 'Distribution') AND "Leg" = 'Single')
            """));
    }
}
