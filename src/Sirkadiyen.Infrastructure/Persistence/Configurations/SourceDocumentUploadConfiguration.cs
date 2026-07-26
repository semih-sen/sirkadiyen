using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

internal sealed class SourceDocumentUploadConfiguration
    : IEntityTypeConfiguration<SourceDocumentUpload>
{
    public void Configure(EntityTypeBuilder<SourceDocumentUpload> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("source_document_uploads");
        builder.HasKey(upload => upload.Id);

        builder.Property(upload => upload.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();

        builder.Property(upload => upload.UploadedBy)
            .HasMaxLength(SourceDocumentUpload.MaximumActorLength)
            .IsRequired();
        builder.Property(upload => upload.FileName)
            .HasMaxLength(SourceDocumentUpload.MaximumFileNameLength)
            .IsRequired();
        builder.Property(upload => upload.ContentSha256)
            .HasMaxLength(SourceDocumentUpload.ContentHashLength)
            .IsRequired();
        builder.Property(upload => upload.CorrelationId)
            .HasMaxLength(SourceDocumentUpload.MaximumCorrelationIdLength)
            .IsRequired();
        builder.Property(upload => upload.Outcome)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne<ScheduleSource>()
            .WithMany()
            .HasForeignKey(upload => upload.ScheduleSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SourceSnapshot>()
            .WithMany()
            .HasForeignKey(upload => upload.SnapshotId)
            .OnDelete(DeleteBehavior.Restrict);

        // The audit question is "what has been uploaded for this source, newest
        // first", which is what an administrator asks after an upload.
        builder.HasIndex(upload => new { upload.SourceId, upload.UploadedAtUtc })
            .IsDescending(false, true);
    }
}
