using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Configurations;

/// <summary>
/// Adds the deferred FK from <c>finance_transactions.FinanceDistributionId</c> to
/// <c>finance_distributions</c>, now that the latter is part of the model (ADR-093). The column and
/// its <c>ck_finance_transactions_distribution_link</c> check already exist from
/// <c>AddFinanceLedger</c>.
/// </summary>
internal sealed class FinanceTransactionDistributionLinkConfiguration
    : IEntityTypeConfiguration<FinanceTransaction>
{
    public void Configure(EntityTypeBuilder<FinanceTransaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasOne<FinanceDistribution>()
            .WithMany()
            .HasForeignKey(transaction => transaction.FinanceDistributionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
