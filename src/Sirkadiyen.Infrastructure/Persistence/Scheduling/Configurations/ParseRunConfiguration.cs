using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

internal sealed class ParseRunConfiguration : IEntityTypeConfiguration<ParseRun>
{
    public void Configure(EntityTypeBuilder<ParseRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("parse_runs");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.ParserProfile).HasMaxLength(100).IsRequired();
        builder.Property(run => run.ParserProfileVersion).HasMaxLength(20).IsRequired();
        builder.Property(run => run.CorrelationId).HasMaxLength(100).IsRequired();

        // Required but allowed to be empty: every run states what companion
        // evidence it read, and "read none" is a statement, not an absence. A
        // nullable column would defeat the uniqueness key below, because
        // PostgreSQL treats NULLs in a unique index as distinct from each other.
        builder.Property(run => run.CompanionFingerprint)
            .HasMaxLength(ParseRunCompanionFingerprint.MaxLength)
            .IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(run => run.AttemptCount).IsRequired();
        builder.Property(run => run.FailureReason).HasMaxLength(2000);

        // Nullable on purpose: a run that never needed recovering must be
        // distinguishable from one that was recovered.
        builder.Property(run => run.LastStaleRecoveryAtUtc);

        // An empty response is valid only while the run is open, so the column
        // is nullable in storage and normalized to an empty string in code.
        builder.Property(run => run.Response).HasColumnType("jsonb");

        builder.HasOne<SourceSnapshot>()
            .WithMany()
            .HasForeignKey(run => run.SourceSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Re-running one profile version against one set of inputs must be
        // deterministic, so a second run of the same inputs is a defect rather
        // than new information. The companion fingerprint is part of the inputs:
        // an edited companion document is genuinely new evidence and must open
        // its own run rather than be short-circuited as already parsed.
        builder.HasIndex(run => new
        {
            run.SourceSnapshotId,
            run.ParserProfile,
            run.ParserProfileVersion,
            run.CompanionFingerprint,
        }).IsUnique();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_parse_runs_attempt_count",
            "\"AttemptCount\" > 0"));
    }
}
