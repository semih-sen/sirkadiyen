using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

/// <summary>Converts the <see cref="SourceId"/> domain type to a stored string.</summary>
internal sealed class SourceIdConverter() : ValueConverter<SourceId, string>(
    sourceId => sourceId.Value,
    value => SourceId.Parse(value));

/// <summary>Stores the declared audience selectors as a JSONB document.</summary>
internal sealed class AudienceSelectorMapConverter()
    : ValueConverter<IReadOnlyDictionary<string, IReadOnlyList<string>>?, string?>(
        map => JsonSerializer.Serialize(map, SerializerOptions),
        json => JsonSerializer
            .Deserialize<Dictionary<string, IReadOnlyList<string>>>(json!, SerializerOptions)!)
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();
}

/// <summary>
/// Compares declared selector maps by value.
/// </summary>
/// <remarks>
/// Without this, change tracking would compare dictionary references and either
/// miss an edit or rewrite the column on every save.
/// </remarks>
internal sealed class AudienceSelectorMapComparer()
    : ValueComparer<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(
        (left, right) => Equal(left, right),
        map => HashOf(map),
        map => CopyOf(map))
{
    private static bool Equal(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? left,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Count == right.Count
            && left.All(entry =>
                right.TryGetValue(entry.Key, out IReadOnlyList<string>? values)
                && entry.Value.SequenceEqual(values, StringComparer.Ordinal));
    }

    private static int HashOf(IReadOnlyDictionary<string, IReadOnlyList<string>>? map)
    {
        if (map is null)
        {
            return 0;
        }

        HashCode hash = default;
        foreach ((string dimension, IReadOnlyList<string> values) in map.OrderBy(
            entry => entry.Key,
            StringComparer.Ordinal))
        {
            hash.Add(dimension, StringComparer.Ordinal);
            foreach (string value in values)
            {
                hash.Add(value, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? CopyOf(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? map) =>
        map is null
            ? null
            : map.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value.ToList(),
                StringComparer.Ordinal);
}

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
