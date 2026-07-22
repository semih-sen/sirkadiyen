using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Asserts the mapping decisions that the schema depends on.
/// </summary>
/// <remarks>
/// These run without a database. They exist because a lost index or a dropped
/// unique constraint does not fail any behavioural test until production data
/// is large enough or concurrent enough to expose it.
/// </remarks>
public sealed class ScheduleModelTests
{
    private static readonly IModel Model = CreateContext().Model;

    [Fact]
    public void EveryEntityLivesInTheApplicationSchema()
    {
        foreach (IEntityType entityType in Model.GetEntityTypes())
        {
            Assert.Equal(SirkadiyenDbContext.SchemaName, entityType.GetSchema());
        }
    }

    [Fact]
    public void SourceIdentifiersAreUnique()
    {
        IEntityType source = Model.FindEntityType(typeof(ScheduleSource))!;

        IIndex index = Assert.Single(
            source.GetIndexes(),
            candidate => candidate.Properties.Any(property => property.Name == "SourceId"));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void OnlyOneRevisionPerSourceMayBePublished()
    {
        IEntityType revision = Model.FindEntityType(typeof(ScheduleRevision))!;

        IIndex index = Assert.Single(
            revision.GetIndexes(),
            candidate => candidate.GetDatabaseName()
                == "ix_schedule_revisions_one_published_per_source");

        Assert.True(index.IsUnique);
        Assert.Equal("\"State\" = 'Published'", index.GetFilter());
    }

    [Fact]
    public void ARevisionMayNotHoldOneLogicalLessonTwice()
    {
        IEntityType record = Model.FindEntityType(typeof(CanonicalScheduleRecord))!;

        IIndex index = Assert.Single(
            record.GetIndexes(),
            candidate => candidate.IsUnique
                && candidate.Properties.Any(property => property.Name == "StableIdentity"));

        Assert.Equal(
            ["ScheduleRevisionId", "StableIdentity"],
            index.Properties.Select(property => property.Name));
    }

    [Fact]
    public void OneParserProfileVersionRunsOncePerSnapshot()
    {
        IEntityType run = Model.FindEntityType(typeof(Domain.ScheduleParsing.ParseRun))!;

        IIndex index = Assert.Single(run.GetIndexes(), candidate => candidate.IsUnique);

        Assert.Equal(
            ["SourceSnapshotId", "ParserProfile", "ParserProfileVersion"],
            index.Properties.Select(property => property.Name));
    }

    [Fact]
    public void ParserAttemptsAndCandidateStatusArePersistedExplicitly()
    {
        IEntityType run = Model.FindEntityType(typeof(Domain.ScheduleParsing.ParseRun))!;
        IEntityType record = Model.FindEntityType(typeof(CanonicalScheduleRecord))!;

        Assert.NotNull(run.FindProperty("AttemptCount"));
        Assert.Equal(
            typeof(string),
            record.FindProperty("RecordStatus")!.GetProviderClrType());

        IIndex candidateIndex = Assert.Single(
            record.GetIndexes(),
            candidate => candidate.IsUnique
                && candidate.Properties.Any(property => property.Name == "CandidateId"));
        Assert.Equal(
            ["ScheduleRevisionId", "CandidateId"],
            candidateIndex.Properties.Select(property => property.Name));
    }

    [Theory]
    [InlineData(typeof(Domain.ScheduleIngestion.SourceSnapshot), "Payload")]
    [InlineData(typeof(CanonicalScheduleRecord), "AudienceSelectors")]
    [InlineData(typeof(CanonicalScheduleRecord), "Evidence")]
    public void EvidenceDocumentsAreStoredAsQueryableJson(Type entityType, string propertyName)
    {
        IProperty property = Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        Assert.Equal("jsonb", property.GetColumnType());
    }

    [Fact]
    public void EnumsAreStoredByNameSoTheirNumbersMayChange()
    {
        IProperty state = Model.FindEntityType(typeof(ScheduleRevision))!.FindProperty("State")!;

        Assert.Equal(typeof(string), state.GetProviderClrType());
    }

    [Fact]
    public void ContestedRowsCarryAConcurrencyToken()
    {
        foreach (Type entityType in new[] { typeof(ScheduleSource), typeof(ScheduleRevision) })
        {
            IProperty rowVersion =
                Model.FindEntityType(entityType)!.FindProperty("RowVersion")!;

            Assert.True(rowVersion.IsConcurrencyToken);
        }
    }

    private static SirkadiyenDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SirkadiyenDbContext>()
            .UseNpgsql("Host=model-only;Database=model-only;Username=model;Password=model")
            .Options);
}
