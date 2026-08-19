using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

internal sealed class ScheduleSourceCatalogRevisionConfiguration
    : IEntityTypeConfiguration<ScheduleSourceCatalogRevision>
{
    public void Configure(EntityTypeBuilder<ScheduleSourceCatalogRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("schedule_source_catalog_revisions");
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).ValueGeneratedNever();

        // A string, like every other status in this schema, so a new kind is a code change rather
        // than a data migration.
        builder.Property(revision => revision.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Text, not jsonb: this column holds the operator's document byte for byte, and jsonb
        // would normalize whitespace and key order, so a restore would no longer reproduce the
        // file the hash was computed over.
        builder.Property(revision => revision.Content)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(revision => revision.ContentHash)
            .HasMaxLength(ScheduleSourceCatalogRevision.MaximumContentHashLength)
            .IsRequired();
        builder.Property(revision => revision.PreviousContentHash)
            .HasMaxLength(ScheduleSourceCatalogRevision.MaximumContentHashLength);
        builder.Property(revision => revision.ActorEmail)
            .HasMaxLength(ScheduleSourceCatalogRevision.MaximumActorEmailLength);
        builder.Property(revision => revision.Reason)
            .HasMaxLength(ScheduleSourceCatalogRevision.MaximumReasonLength);
        builder.Property(revision => revision.CorrelationId)
            .HasMaxLength(ScheduleSourceCatalogRevision.MaximumCorrelationIdLength);
        builder.Property(revision => revision.ChangeSummary)
            .HasColumnType("jsonb");

        // The actor is a soft reference for the same reason it is on an audit event: the history
        // must outlive the account that wrote it.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(revision => revision.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(revision => revision.RecordedAtUtc);
        builder.HasIndex(revision => revision.ContentHash);
    }
}
