using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Infrastructure.Persistence.Auditing.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_events");
        builder.HasKey(auditEvent => auditEvent.Id);
        builder.Property(auditEvent => auditEvent.Id).ValueGeneratedNever();

        // Stored as a string, without a check constraint, so a new category is a code change and
        // not a data migration. The category is indexed because the access-log view filters by it.
        builder.Property(auditEvent => auditEvent.Category)
            .HasConversion<string>()
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(auditEvent => auditEvent.ActorEmail)
            .HasMaxLength(AuditEvent.MaximumActorEmailLength);
        builder.Property(auditEvent => auditEvent.SubjectType)
            .HasMaxLength(AuditEvent.MaximumSubjectTypeLength);
        builder.Property(auditEvent => auditEvent.SubjectId)
            .HasMaxLength(AuditEvent.MaximumSubjectIdLength);
        builder.Property(auditEvent => auditEvent.CorrelationId)
            .HasMaxLength(AuditEvent.MaximumCorrelationIdLength);
        builder.Property(auditEvent => auditEvent.MaskedIp)
            .HasMaxLength(AuditEvent.MaximumMaskedIpLength);
        builder.Property(auditEvent => auditEvent.UserAgent)
            .HasMaxLength(AuditEvent.MaximumUserAgentLength);
        builder.Property(auditEvent => auditEvent.Reason)
            .HasMaxLength(AuditEvent.MaximumReasonLength);
        builder.Property(auditEvent => auditEvent.Metadata)
            .HasColumnType("jsonb");

        // The actor is a soft reference: the audit trail must outlive an account, so no cascade
        // and no required foreign key. Anonymous/system events carry no actor at all.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(auditEvent => auditEvent.OccurredAtUtc);
        builder.HasIndex(auditEvent => auditEvent.Category);
        builder.HasIndex(auditEvent => auditEvent.ActorUserId);
    }
}
