using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class CanonicalScheduleRecordConfiguration
    : IEntityTypeConfiguration<CanonicalScheduleRecord>
{
    public void Configure(EntityTypeBuilder<CanonicalScheduleRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("canonical_schedule_records");
        builder.HasKey(record => record.Id);

        builder.Property(record => record.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();

        builder.Property(record => record.AcademicYear).HasMaxLength(20).IsRequired();
        builder.Property(record => record.ProgramLanguage).HasConversion<string>().HasMaxLength(20)
            .IsRequired();
        builder.Property(record => record.EventType).HasConversion<string>().HasMaxLength(40)
            .IsRequired();
        builder.Property(record => record.AudienceScope).HasConversion<string>().HasMaxLength(40)
            .IsRequired();
        builder.Property(record => record.AudienceSelectors).HasColumnType("jsonb").IsRequired();
        builder.Property(record => record.Evidence).HasColumnType("jsonb").IsRequired();
        builder.Property(record => record.DisplayTitle).HasMaxLength(1000).IsRequired();
        builder.Property(record => record.NormalizedCourseIdentity).HasMaxLength(500);
        builder.Property(record => record.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(record => record.Instructor).HasMaxLength(1000);
        builder.Property(record => record.Location).HasMaxLength(1000);
        builder.Property(record => record.StableIdentity).HasMaxLength(100).IsRequired();
        builder.Property(record => record.ContentHash).HasMaxLength(100).IsRequired();
        builder.Property(record => record.Confidence).HasPrecision(4, 3);

        builder.HasOne<ScheduleRevision>()
            .WithMany()
            .HasForeignKey(record => record.ScheduleRevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        // One revision may not claim the same logical lesson twice. The parser
        // refuses duplicates, and the schema makes sure a future producer cannot
        // reintroduce them.
        builder.HasIndex(record => new { record.ScheduleRevisionId, record.StableIdentity })
            .IsUnique();

        // The diff engine loads one revision ordered by date, and audience
        // resolution filters by cohort and date.
        builder.HasIndex(record => new { record.ScheduleRevisionId, record.LocalDate });
        builder.HasIndex(record => new
        {
            record.SourceId,
            record.ClassYear,
            record.ProgramLanguage,
            record.LocalDate,
        });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_canonical_schedule_records_time_order",
            "\"EndLocalTime\" > \"StartLocalTime\""));
    }
}
