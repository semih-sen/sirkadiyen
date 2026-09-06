using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

internal sealed class RevisionValidationFindingConfiguration
    : IEntityTypeConfiguration<RevisionValidationFinding>
{
    public void Configure(EntityTypeBuilder<RevisionValidationFinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("revision_validation_findings");
        builder.HasKey(finding => finding.Id);

        // Rules and severities are stored as text so that a new rule does not
        // renumber the existing ones and silently change the meaning of stored
        // history (AI_GUIDELINE section 18).
        builder.Property(finding => finding.Rule).HasConversion<string>().HasMaxLength(60)
            .IsRequired();
        builder.Property(finding => finding.Severity).HasConversion<string>().HasMaxLength(20)
            .IsRequired();

        builder.Property(finding => finding.Message).HasMaxLength(2000).IsRequired();

        // Nullable, not required: a finding with no machine-readable evidence stores SQL NULL.
        // jsonb has no empty value, so the alternative — an empty string — is rejected as invalid
        // JSON (22P02) and cannot be the default here.
        builder.Property(finding => finding.Detail).HasColumnType("jsonb");

        // A finding is meaningless without its revision, and a revision's
        // findings are the audit trail for why it was held, so they are deleted
        // only when the revision itself is.
        builder.HasOne<ScheduleRevision>()
            .WithMany()
            .HasForeignKey(finding => finding.ScheduleRevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(finding => new { finding.ScheduleRevisionId, finding.CreatedAtUtc });
    }
}
