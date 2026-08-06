using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

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
