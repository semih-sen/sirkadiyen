using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

/// <summary>Stores <see cref="FinanceAudit.ChangedFields"/> as a JSONB string array.</summary>
internal sealed class ChangedFieldsConverter()
    : ValueConverter<IReadOnlyList<string>, string>(
        fields => JsonSerializer.Serialize(fields, SerializerOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, SerializerOptions)!)
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();
}

/// <summary>Compares changed-field lists by value, in order, for change tracking.</summary>
internal sealed class ChangedFieldsComparer()
    : ValueComparer<IReadOnlyList<string>>(
        (left, right) => Equal(left, right),
        fields => HashOf(fields),
        fields => CopyOf(fields))
{
    private static bool Equal(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static int HashOf(IReadOnlyList<string>? fields)
    {
        if (fields is null)
        {
            return 0;
        }

        HashCode hash = default;
        foreach (string field in fields)
        {
            hash.Add(field, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyList<string> CopyOf(IReadOnlyList<string>? fields) =>
        fields is null ? [] : [.. fields];
}

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
        // FinanceTransactionDistributionLinkConfiguration below: that table did not exist when this
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

internal sealed class FinanceAuditConfiguration : IEntityTypeConfiguration<FinanceAudit>
{
    private const string ReasonRequiredActions =
        "'AccountClosed', 'HolderDeactivated', 'TransactionUpdated', 'TransactionDeleted', " +
        "'ObligationSettlementCancelled', 'ObligationWrittenOff', 'ObligationCancelled', " +
        "'DistributionExecuted', 'DistributionReversed'";

    public void Configure(EntityTypeBuilder<FinanceAudit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("finance_audits");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Id).ValueGeneratedNever();
        builder.Property(audit => audit.Sequence)
            .ValueGeneratedOnAdd()
            .UseIdentityAlwaysColumn();
        builder.Property(audit => audit.Action)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(audit => audit.SubjectType)
            .HasMaxLength(FinanceAudit.MaximumSubjectTypeLength)
            .IsRequired();
        builder.Property(audit => audit.ActorEmail)
            .HasMaxLength(FinanceAudit.MaximumActorEmailLength)
            .IsRequired();
        builder.Property(audit => audit.CorrelationId)
            .HasMaxLength(FinanceAudit.MaximumCorrelationIdLength);
        builder.Property(audit => audit.Reason)
            .HasMaxLength(FinanceAudit.MaximumReasonLength);
        builder.Property(audit => audit.BeforeState).HasColumnType("jsonb");
        builder.Property(audit => audit.AfterState).HasColumnType("jsonb");
        builder.Property(audit => audit.ChangedFields)
            .HasConversion(new ChangedFieldsConverter())
            .HasColumnType("jsonb")
            .IsRequired()
            .Metadata.SetValueComparer(new ChangedFieldsComparer());
        builder.Property(audit => audit.AmountDelta).HasPrecision(18, 2);
        builder.Property(audit => audit.RevisionNumber).IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(audit => audit.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(audit => new { audit.SubjectType, audit.SubjectId, audit.Sequence });
        builder.HasIndex(audit => audit.OccurredAtUtc);
        builder.HasIndex(audit => audit.ActorUserId);
        builder.HasIndex(audit => new { audit.Action, audit.OccurredAtUtc });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_audits_action",
            """
            "Action" IN ('AccountOpened', 'AccountUpdated', 'AccountClosed', 'HolderCreated',
                         'HolderUpdated', 'HolderDeactivated', 'PartnerSharesChanged',
                         'TransactionCreated', 'TransactionUpdated', 'TransactionDeleted',
                         'ObligationCreated', 'ObligationUpdated', 'ObligationSettled',
                         'ObligationSettlementCancelled', 'ObligationWrittenOff', 'ObligationCancelled',
                         'DistributionExecuted', 'DistributionReversed')
            """));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_finance_audits_reason_required",
            $"\"Action\" NOT IN ({ReasonRequiredActions}) OR \"Reason\" IS NOT NULL"));
    }
}

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
