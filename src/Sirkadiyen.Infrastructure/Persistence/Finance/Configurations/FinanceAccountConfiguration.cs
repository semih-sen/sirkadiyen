using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Configurations;

internal sealed class FinanceAccountConfiguration : IEntityTypeConfiguration<FinanceAccount>
{
    public void Configure(EntityTypeBuilder<FinanceAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_accounts");
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.Name)
            .HasMaxLength(FinanceAccount.MaximumNameLength)
            .IsRequired();
        builder.Property(account => account.Kind)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(account => account.CurrencyCode)
            .HasColumnType("char(3)")
            .IsRequired();
        builder.Property(account => account.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(account => account.ClosedReason)
            .HasMaxLength(FinanceAccount.MaximumClosedReasonLength);
        builder.Property(account => account.RowVersion).IsRowVersion();

        builder.HasOne<FinanceAccountHolder>()
            .WithMany()
            .HasForeignKey(account => account.FinanceAccountHolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(account => new { account.FinanceAccountHolderId, account.Name }).IsUnique();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_accounts_kind",
            "\"Kind\" IN ('Cash', 'Bank')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_accounts_status",
            "\"Status\" IN ('Active', 'Closed')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_accounts_currency",
            $"\"CurrencyCode\" = '{FinanceAccount.SupportedCurrencyCode}'"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_accounts_closure",
            """
            ("Status" = 'Closed' AND "ClosedAtUtc" IS NOT NULL AND "ClosedReason" IS NOT NULL)
            OR
            ("Status" <> 'Closed' AND "ClosedAtUtc" IS NULL AND "ClosedReason" IS NULL)
            """));
    }
}
