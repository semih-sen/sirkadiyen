using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class ScheduleDiffEntryConfiguration : IEntityTypeConfiguration<ScheduleDiffEntry>
{
    public void Configure(EntityTypeBuilder<ScheduleDiffEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("schedule_diff_entries");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Change).HasConversion<string>().HasMaxLength(40)
            .IsRequired();
        builder.Property(entry => entry.Match).HasConversion<string>().HasMaxLength(40)
            .IsRequired();

        // Scores are the evidence behind a secondary match, so they are stored
        // with the precision the differ rounded them to rather than as a float
        // whose value would drift between reads.
        builder.Property(entry => entry.MatchScore).HasPrecision(5, 4);
        builder.Property(entry => entry.TitleScore).HasPrecision(5, 4);
        builder.Property(entry => entry.InstructorScore).HasPrecision(5, 4);
        builder.Property(entry => entry.DepartmentScore).HasPrecision(5, 4);

        // An entry points at the canonical records it classified, and those
        // records must outlive it: they are what a calendar operation is derived
        // from and what an audit reads afterwards.
        builder.HasOne<CanonicalScheduleRecord>()
            .WithMany()
            .HasForeignKey(entry => entry.PreviousRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CanonicalScheduleRecord>()
            .WithMany()
            .HasForeignKey(entry => entry.CurrentRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        // Synchronization reads one diff's actionable entries; unchanged ones
        // are the bulk of a normal diff and are never dispatched.
        builder.HasIndex(entry => new { entry.ScheduleDiffId, entry.Change });

        // A record may be classified only once within a diff, on either side.
        builder.HasIndex(entry => new { entry.ScheduleDiffId, entry.PreviousRecordId })
            .IsUnique();
        builder.HasIndex(entry => new { entry.ScheduleDiffId, entry.CurrentRecordId })
            .IsUnique();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_schedule_diff_entries_record_presence",
            "\"PreviousRecordId\" IS NOT NULL OR \"CurrentRecordId\" IS NOT NULL"));
    }
}
