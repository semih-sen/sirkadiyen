using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class FinanceObligationConfiguration : IEntityTypeConfiguration<FinanceObligation>
{
    public void Configure(EntityTypeBuilder<FinanceObligation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_obligations");
        builder.HasKey(obligation => obligation.Id);
        builder.Property(obligation => obligation.Id).ValueGeneratedNever();
        builder.Property(obligation => obligation.Direction)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(obligation => obligation.Category)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(obligation => obligation.CounterpartyName)
            .HasMaxLength(FinanceObligation.MaximumCounterpartyNameLength)
            .IsRequired();
        builder.Property(obligation => obligation.Description)
            .HasMaxLength(FinanceObligation.MaximumDescriptionLength);
        builder.Property(obligation => obligation.Amount).HasPrecision(18, 2);
        builder.Property(obligation => obligation.SettledAmount).HasPrecision(18, 2);
        builder.Property(obligation => obligation.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(obligation => obligation.ClosureReason)
            .HasMaxLength(FinanceObligation.MaximumClosureReasonLength);
        builder.Property(obligation => obligation.CreatedByEmail)
            .HasMaxLength(FinanceObligation.MaximumActorEmailLength)
            .IsRequired();
        builder.Property(obligation => obligation.RowVersion).IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(obligation => obligation.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(obligation => new { obligation.Direction, obligation.Status, obligation.DueOn });
        builder.HasIndex(obligation => obligation.IssuedOn);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_obligations_direction",
            "\"Direction\" IN ('Receivable', 'Payable')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_obligations_amount",
            "\"Amount\" > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_obligations_settled",
            "\"SettledAmount\" >= 0 AND \"SettledAmount\" <= \"Amount\""));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_obligations_dates",
            "\"DueOn\" IS NULL OR \"DueOn\" >= \"IssuedOn\""));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_obligations_direction_category",
            """
            ("Direction" = 'Receivable' AND "Category" IN ('LicenseSales', 'Sponsorship', 'Donation', 'OtherIncome'))
            OR ("Direction" = 'Payable' AND "Category" IN ('Servers', 'Domains', 'ExternalServices',
                                            'SoftwareLicenses', 'Marketing', 'Operational', 'Charitable', 'OtherExpense'))
            """));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_obligations_status",
            """
            ("Status" = 'Open'             AND "SettledAmount" = 0)
            OR ("Status" = 'PartiallySettled' AND "SettledAmount" > 0 AND "SettledAmount" < "Amount")
            OR ("Status" = 'Settled'          AND "SettledAmount" = "Amount")
            OR ("Status" = 'WrittenOff'       AND "WrittenOffOn" IS NOT NULL AND "ClosureReason" IS NOT NULL)
            OR ("Status" = 'Cancelled'        AND "CancelledOn" IS NOT NULL AND "ClosureReason" IS NOT NULL
                                               AND "SettledAmount" = 0)
            """));
    }
}
