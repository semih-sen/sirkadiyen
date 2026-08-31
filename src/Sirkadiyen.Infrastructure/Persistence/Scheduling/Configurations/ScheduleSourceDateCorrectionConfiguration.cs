using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

internal sealed class ScheduleSourceDateCorrectionConfiguration
    : IEntityTypeConfiguration<ScheduleSourceDateCorrection>
{
    public void Configure(EntityTypeBuilder<ScheduleSourceDateCorrection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("schedule_source_date_corrections");
        builder.HasKey(correction => correction.Id);

        builder.Property(correction => correction.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();

        builder.Property(correction => correction.Original).IsRequired();
        builder.Property(correction => correction.Corrected).IsRequired();

        builder.Property(correction => correction.DecidedBy)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(correction => correction.DecidedAtUtc).IsRequired();

        builder.Property(correction => correction.Note)
            .HasMaxLength(ScheduleSourceDateCorrection.MaximumNoteLength)
            .IsRequired();

        // One source states one wrong date once. Two corrections of the same
        // value would make the parse's reading of it depend on which row the
        // query happened to return first, which is exactly the ambiguity the
        // parser refuses to resolve on its own (ADR-139).
        builder.HasIndex(correction => new { correction.SourceId, correction.Original })
            .IsUnique();
    }
}
