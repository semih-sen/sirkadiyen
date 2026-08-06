using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

internal sealed class SourceSnapshotConfiguration : IEntityTypeConfiguration<SourceSnapshot>
{
    public void Configure(EntityTypeBuilder<SourceSnapshot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("source_snapshots");
        builder.HasKey(snapshot => snapshot.Id);

        builder.Property(snapshot => snapshot.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();

        builder.Property(snapshot => snapshot.ExternalSnapshotId).HasMaxLength(200).IsRequired();
        builder.Property(snapshot => snapshot.SpreadsheetId).HasMaxLength(200).IsRequired();
        builder.Property(snapshot => snapshot.AcademicYear).HasMaxLength(20).IsRequired();
        builder.Property(snapshot => snapshot.ContentHash).HasMaxLength(100).IsRequired();
        builder.Property(snapshot => snapshot.ContractVersion).HasMaxLength(20).IsRequired();

        // The payload is stored as jsonb so a snapshot can be inspected and
        // queried in place. It is never overwritten; retention may explicitly
        // remove it while preserving this metadata row and the downstream
        // parse/revision/diff evidence.
        builder.Property(snapshot => snapshot.Payload).HasColumnType("jsonb");

        builder.HasOne<Domain.Scheduling.Sources.ScheduleSource>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ScheduleSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Change detection reads the newest snapshot for one source, so the
        // index is ordered to serve exactly that query.
        builder.HasIndex(snapshot => new { snapshot.SourceId, snapshot.AcquiredAtUtc })
            .IsDescending(false, true);

        builder.HasIndex(snapshot => new { snapshot.SourceId, snapshot.ContentHash });
        builder.HasIndex(snapshot => new
        {
            snapshot.ScheduleSourceId,
            snapshot.AcademicYear,
            snapshot.AcquiredAtUtc,
        });
    }
}
