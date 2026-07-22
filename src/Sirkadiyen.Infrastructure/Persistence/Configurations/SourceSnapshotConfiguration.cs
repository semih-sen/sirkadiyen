using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

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
        builder.Property(snapshot => snapshot.ContentHash).HasMaxLength(100).IsRequired();
        builder.Property(snapshot => snapshot.ContractVersion).HasMaxLength(20).IsRequired();

        // The payload is stored as jsonb so a snapshot can be inspected and
        // queried in place. It is never updated: a snapshot is evidence.
        builder.Property(snapshot => snapshot.Payload).HasColumnType("jsonb").IsRequired();

        builder.HasOne<Domain.ScheduleSources.ScheduleSource>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ScheduleSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Change detection reads the newest snapshot for one source, so the
        // index is ordered to serve exactly that query.
        builder.HasIndex(snapshot => new { snapshot.SourceId, snapshot.AcquiredAtUtc })
            .IsDescending(false, true);

        builder.HasIndex(snapshot => new { snapshot.SourceId, snapshot.ContentHash });
    }
}
