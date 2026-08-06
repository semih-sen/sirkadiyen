using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

internal sealed class ScheduleSourceConfiguration : IEntityTypeConfiguration<ScheduleSource>
{
    public void Configure(EntityTypeBuilder<ScheduleSource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("schedule_sources");
        builder.HasKey(source => source.Id);

        builder.Property(source => source.SourceId)
            .HasConversion(new SourceIdConverter())
            .HasMaxLength(SourceId.MaxLength)
            .IsRequired();

        // The natural identifier is what configuration, evidence and audit
        // records refer to, so the database enforces that it stays unique.
        builder.HasIndex(source => source.SourceId).IsUnique();

        builder.Property(source => source.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(source => source.Transport).HasConversion<string>().HasMaxLength(40)
            .IsRequired();
        builder.Property(source => source.DocumentFormat).HasConversion<string>().HasMaxLength(40)
            .IsRequired();
        builder.Property(source => source.SourceUri).HasMaxLength(2048).IsRequired();
        builder.Property(source => source.ExternalId).HasMaxLength(200);
        builder.Property(source => source.ParserProfile).HasMaxLength(100).IsRequired();
        builder.Property(source => source.ParserProfileVersion).HasMaxLength(20).IsRequired();
        builder.Property(source => source.AcademicYear).HasMaxLength(20).IsRequired();
        builder.Property(source => source.ProgramLanguage).HasConversion<string>().HasMaxLength(20)
            .IsRequired();
        builder.Property(source => source.TimeZoneId).HasMaxLength(100).IsRequired();

        // The declared cohorts are a document whose dimensions differ per source
        // and will be superseded by the ADR-027 profile schema, so they are stored
        // as JSONB rather than as columns. Null means "not declared", which
        // revision validation must be able to tell apart from "declared empty".
        builder.Property(source => source.SupportedAudienceSelectors)
            .HasConversion(new AudienceSelectorMapConverter())
            .Metadata.SetValueComparer(new AudienceSelectorMapComparer());
        builder.Property(source => source.SupportedAudienceSelectors).HasColumnType("jsonb");

        // One administrative upload resolves its targets by this name, so it is
        // indexed for that lookup rather than scanned (ADR-080).
        builder.Property(source => source.SharedDocumentGroup)
            .HasMaxLength(ScheduleSource.MaximumSharedDocumentGroupLength);
        builder.HasIndex(source => source.SharedDocumentGroup)
            .HasFilter("\"SharedDocumentGroup\" IS NOT NULL");

        // PostgreSQL maintains xmin itself, which gives optimistic concurrency
        // without an application-managed version column.
        builder.Property(source => source.RowVersion).IsRowVersion();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_schedule_sources_class_year",
            "\"ClassYear\" BETWEEN 1 AND 6"));

        builder.HasIndex(source => source.IsPollingEnabled)
            .HasFilter("\"IsPollingEnabled\"");
    }
}
