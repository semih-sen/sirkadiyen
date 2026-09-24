using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sirkadiyen.Infrastructure.Persistence.Vault;

internal sealed class VaultNoteJobRowConfiguration : IEntityTypeConfiguration<VaultNoteJobRow>
{
    public void Configure(EntityTypeBuilder<VaultNoteJobRow> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("vault_note_jobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();

        // Source and status are stored as strings without check constraints, so a new value is a code
        // change and not a data migration.
        builder.Property(job => job.Source).HasMaxLength(20).IsRequired();
        builder.Property(job => job.Status).HasMaxLength(20).IsRequired();
        builder.Property(job => job.RequestedBy).HasMaxLength(VaultNoteJobRow.MaximumRequestedByLength);
        builder.Property(job => job.Prompt).HasMaxLength(VaultNoteJobRow.MaximumPromptLength).IsRequired();
        builder.Property(job => job.Folder).HasMaxLength(VaultNoteJobRow.MaximumPathLength);
        builder.Property(job => job.Title).HasMaxLength(VaultNoteJobRow.MaximumPathLength);
        builder.Property(job => job.NotePath).HasMaxLength(VaultNoteJobRow.MaximumPathLength);
        builder.Property(job => job.NoteLink).HasMaxLength(VaultNoteJobRow.MaximumPathLength);
        builder.Property(job => job.Backlinks).HasColumnType("jsonb").IsRequired();
        builder.Property(job => job.Warnings).HasColumnType("jsonb").IsRequired();

        // The panel lists newest first; the startup recovery reads the unfinished ones.
        builder.HasIndex(job => job.CreatedAtUtc);
        builder.HasIndex(job => job.Status);
    }
}
