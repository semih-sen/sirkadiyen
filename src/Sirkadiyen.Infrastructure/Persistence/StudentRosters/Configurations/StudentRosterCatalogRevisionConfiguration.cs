using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.StudentRosters;

namespace Sirkadiyen.Infrastructure.Persistence.StudentRosters.Configurations;

internal sealed class StudentRosterCatalogRevisionConfiguration
    : IEntityTypeConfiguration<StudentRosterCatalogRevision>
{
    public void Configure(EntityTypeBuilder<StudentRosterCatalogRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("student_roster_catalog_revisions");
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
            .HasMaxLength(StudentRosterCatalogRevision.MaximumContentHashLength)
            .IsRequired();
        builder.Property(revision => revision.PreviousContentHash)
            .HasMaxLength(StudentRosterCatalogRevision.MaximumContentHashLength);
        builder.Property(revision => revision.ActorEmail)
            .HasMaxLength(StudentRosterCatalogRevision.MaximumActorEmailLength);
        builder.Property(revision => revision.Reason)
            .HasMaxLength(StudentRosterCatalogRevision.MaximumReasonLength);
        builder.Property(revision => revision.CorrelationId)
            .HasMaxLength(StudentRosterCatalogRevision.MaximumCorrelationIdLength);
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
