using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

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

        // Re-running one profile version against one snapshot must be
        // deterministic, so a second run of the same pair is a defect rather
        // than new information.
        builder.HasIndex(run => new
        {
            run.SourceSnapshotId,
            run.ParserProfile,
            run.ParserProfileVersion,
        }).IsUnique();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_parse_runs_attempt_count",
            "\"AttemptCount\" > 0"));
    }
}
