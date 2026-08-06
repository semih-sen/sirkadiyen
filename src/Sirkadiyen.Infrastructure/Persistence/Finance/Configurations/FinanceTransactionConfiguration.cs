using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class FinanceTransactionConfiguration : IEntityTypeConfiguration<FinanceTransaction>
{
    public void Configure(EntityTypeBuilder<FinanceTransaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_transactions");
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Id).ValueGeneratedNever();
        builder.Property(transaction => transaction.Kind)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(transaction => transaction.Category)
            .HasConversion<string>()
            .HasMaxLength(40);
        builder.Property(transaction => transaction.Amount).HasPrecision(18, 2);
        builder.Property(transaction => transaction.Description)
            .HasMaxLength(FinanceTransaction.MaximumDescriptionLength)
            .IsRequired();
        builder.Property(transaction => transaction.Reference)
            .HasMaxLength(FinanceTransaction.MaximumReferenceLength);
        builder.Property(transaction => transaction.CounterpartyName)
            .HasMaxLength(FinanceTransaction.MaximumCounterpartyNameLength);
        builder.Property(transaction => transaction.RevisionNumber).IsRequired();
        builder.Property(transaction => transaction.CreatedByEmail)
            .HasMaxLength(FinanceTransaction.MaximumActorEmailLength)
            .IsRequired();
        builder.Property(transaction => transaction.UpdatedByEmail)
            .HasMaxLength(FinanceTransaction.MaximumActorEmailLength)
            .IsRequired();
        builder.Property(transaction => transaction.RowVersion).IsRowVersion();

        // The FK to finance_distributions is declared separately, in
        // FinanceTransactionDistributionLinkConfiguration: that table did not exist when this
        // configuration was first written (AddFinanceLedger), and EF cannot model a relationship to
        // a type outside the current model.
        builder.Property(transaction => transaction.FinanceDistributionId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(transaction => transaction.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(transaction => transaction.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(transaction => transaction.OccurredOn);
        builder.HasIndex(transaction => new { transaction.Kind, transaction.OccurredOn });
        builder.HasIndex(transaction => new { transaction.Category, transaction.OccurredOn });
        builder.HasIndex(transaction => transaction.FinanceDistributionId);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_transactions_kind",
            "\"Kind\" IN ('OpeningBalance', 'Income', 'Expense', 'Transfer', 'Distribution')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_transactions_amount",
            "\"Amount\" > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_transactions_revision",
            "\"RevisionNumber\" >= 1"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_transactions_category",
            """
            ("Kind" = 'Income'  AND "Category" IN ('LicenseSales', 'Sponsorship', 'Donation', 'OtherIncome'))
            OR ("Kind" = 'Expense' AND "Category" IN ('Servers', 'Domains', 'ExternalServices',
                                            'SoftwareLicenses', 'Marketing', 'Operational', 'Charitable', 'OtherExpense'))
            OR ("Kind" IN ('OpeningBalance', 'Transfer', 'Distribution') AND "Category" IS NULL)
            """));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_transactions_distribution_link",
            "(\"Kind\" = 'Distribution') = (\"FinanceDistributionId\" IS NOT NULL)"));
    }
}
