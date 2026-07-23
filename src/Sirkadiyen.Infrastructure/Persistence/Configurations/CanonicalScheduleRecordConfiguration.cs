using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stores the department list as JSONB.
/// </summary>
/// <remarks>
/// A lesson names zero, one or several departments, and the order is the source's
/// order. A single text column would force the reader to re-split what the parser
/// already separated, and a child table would add a join for a value that is only
/// ever read with its record.
/// </remarks>
internal sealed class DepartmentListConverter()
    : ValueConverter<IReadOnlyList<string>, string>(
        departments => JsonSerializer.Serialize(departments, SerializerOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, SerializerOptions)!)
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();
}

/// <summary>Compares department lists by value, in order.</summary>
/// <remarks>
/// Without this, change tracking would compare list references and either miss an
/// edit or rewrite the column on every save.
/// </remarks>
internal sealed class DepartmentListComparer()
    : ValueComparer<IReadOnlyList<string>>(
        (left, right) => Equal(left, right),
        departments => HashOf(departments),
        departments => CopyOf(departments))
{
    private static bool Equal(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static int HashOf(IReadOnlyList<string>? departments)
    {
        if (departments is null)
        {
            return 0;
        }

        HashCode hash = default;
        foreach (string department in departments)
        {
            hash.Add(department, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyList<string> CopyOf(IReadOnlyList<string>? departments) =>
        departments is null ? [] : [.. departments];
}

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
        builder.Property(record => record.CandidateId).HasMaxLength(200).IsRequired();
        builder.Property(record => record.RecordStatus).HasConversion<string>().HasMaxLength(40)
            .IsRequired();
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
        builder.Property(record => record.CurriculumBlock).HasMaxLength(500);
        builder.Property(record => record.Departments)
            .HasConversion(new DepartmentListConverter())
            .HasColumnType("jsonb")
            .IsRequired()
            .Metadata.SetValueComparer(new DepartmentListComparer());
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
        builder.HasIndex(record => new { record.ScheduleRevisionId, record.CandidateId })
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
