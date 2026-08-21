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

        // Which half of a shared session this source publishes is catalog configuration of
        // the same shape, stored the same way, and null means "not narrowed" (ADR-110).
        builder.Property(source => source.AuthoritativeAudienceSelectors)
            .HasConversion(new AudienceSelectorMapConverter())
            .Metadata.SetValueComparer(new AudienceSelectorMapComparer());
        builder.Property(source => source.AuthoritativeAudienceSelectors).HasColumnType("jsonb");

        // One administrative upload resolves its targets by this name, so it is
        // indexed for that lookup rather than scanned (ADR-080).
        builder.Property(source => source.SharedDocumentGroup)
            .HasMaxLength(ScheduleSource.MaximumSharedDocumentGroupLength);
        builder.HasIndex(source => source.SharedDocumentGroup)
            .HasFilter("\"SharedDocumentGroup\" IS NOT NULL");

        // Companions are read with their source and never queried on their own,
        // so they are a JSONB array on the row rather than a join table. Empty is
        // the normal case and is stored as an empty array, so "no companions" and
        // "companions not yet reconciled" cannot be confused (ADR-102).
        builder.Property(source => source.CompanionSourceIds)
            .HasConversion(new SourceIdListConverter())
            .HasColumnType("jsonb")
            .IsRequired()
            .Metadata.SetValueComparer(new SourceIdListComparer());

        // The rotation owners are stored the same way and for the same reasons:
        // read with their source, never queried on their own, and empty by
        // default so "defers unconditionally" is a stored fact (ADR-126).
        builder.Property(source => source.GroupRotationSourceIds)
            .HasConversion(new SourceIdListConverter())
            .HasColumnType("jsonb")
            .IsRequired()
            .Metadata.SetValueComparer(new SourceIdListComparer());

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
