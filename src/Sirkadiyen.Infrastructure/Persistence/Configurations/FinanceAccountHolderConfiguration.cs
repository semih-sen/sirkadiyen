using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class FinanceAccountHolderConfiguration : IEntityTypeConfiguration<FinanceAccountHolder>
{
    public void Configure(EntityTypeBuilder<FinanceAccountHolder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_account_holders");
        builder.HasKey(holder => holder.Id);
        builder.Property(holder => holder.Id).ValueGeneratedNever();
        builder.Property(holder => holder.DisplayName)
            .HasMaxLength(FinanceAccountHolder.MaximumDisplayNameLength)
            .IsRequired();
        builder.Property(holder => holder.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(holder => holder.RowVersion).IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(holder => holder.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(holder => holder.DisplayName).IsUnique();
        builder.HasIndex(holder => holder.UserId)
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_account_holders_status",
            "\"Status\" IN ('Active', 'Inactive')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_account_holders_share",
            $"\"ShareBasisPoints\" BETWEEN {FinanceAccountHolder.MinimumShareBasisPoints} " +
            $"AND {FinanceAccountHolder.MaximumShareBasisPoints}"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_account_holders_inactive_has_no_share",
            "\"Status\" = 'Active' OR \"ShareBasisPoints\" = 0"));
    }
}
