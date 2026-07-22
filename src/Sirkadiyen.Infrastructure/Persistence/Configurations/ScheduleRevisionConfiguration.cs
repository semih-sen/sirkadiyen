using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class ScheduleRevisionConfiguration : IEntityTypeConfiguration<ScheduleRevision>
{
    public void Configure(EntityTypeBuilder<ScheduleRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("schedule_revisions");
        builder.HasKey(revision => revision.Id);

        builder.Property(revision => revision.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();

        builder.Property(revision => revision.State).HasConversion<string>().HasMaxLength(40)
            .IsRequired();
        builder.Property(revision => revision.StateReason).HasMaxLength(2000);
        builder.Property(revision => revision.RowVersion).IsRowVersion();

        builder.HasOne<ScheduleSource>()
            .WithMany()
            .HasForeignKey(revision => revision.ScheduleSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ParseRun>()
            .WithMany()
            .HasForeignKey(revision => revision.ParseRunId)
            .OnDelete(DeleteBehavior.Restrict);

        // Only one revision per source may be live. Enforcing it in the schema
        // means a publication bug fails loudly instead of leaving two schedules
        // claiming to be current.
        builder.HasIndex(revision => revision.ScheduleSourceId)
            .IsUnique()
            .HasFilter("\"State\" = 'Published'")
            .HasDatabaseName("ix_schedule_revisions_one_published_per_source");

        builder.HasIndex(revision => new { revision.ScheduleSourceId, revision.CreatedAtUtc });
    }
}
