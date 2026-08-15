using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Sirkadiyen.Domain.Operations;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
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

    /// <summary>
    /// One parser profile version runs once per set of inputs, and a companion
    /// document is an input (ADR-102).
    /// </summary>
    /// <remarks>
    /// The fingerprint has to be part of the key rather than beside it. Without
    /// it, an edited bedside document would leave the annual program short
    /// circuited as already parsed and the corrected topic would never reach a
    /// calendar; and it must be non-nullable, because PostgreSQL treats NULLs in
    /// a unique index as distinct and would permit two runs for one snapshot.
    /// </remarks>
    [Fact]
    public void OneParserProfileVersionRunsOncePerSnapshotAndCompanionSet()
    {
        IEntityType run = Model.FindEntityType(typeof(Domain.Scheduling.Parsing.ParseRun))!;

        IIndex index = Assert.Single(run.GetIndexes(), candidate => candidate.IsUnique);

        Assert.Equal(
            ["SourceSnapshotId", "ParserProfile", "ParserProfileVersion", "CompanionFingerprint"],
            index.Properties.Select(property => property.Name));
        Assert.False(run.FindProperty("CompanionFingerprint")!.IsNullable);
    }

    /// <summary>
    /// The companion list is stored as a JSON array, so "names no companion" and
    /// "has not been reconciled" cannot be confused (ADR-102).
    /// </summary>
    [Fact]
    public void CompanionSourceIdsAreStoredAsARequiredJsonArray()
    {
        IProperty companions = Model.FindEntityType(typeof(ScheduleSource))!
            .FindProperty("CompanionSourceIds")!;

        Assert.False(companions.IsNullable);
        Assert.Equal("jsonb", companions.GetColumnType());
    }

    [Fact]
    public void ParserAttemptsAndCandidateStatusArePersistedExplicitly()
    {
        IEntityType run = Model.FindEntityType(typeof(Domain.Scheduling.Parsing.ParseRun))!;
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

    [Fact]
    public void CurriculumBlockIsOptionalAndBounded()
    {
        IProperty block = Model.FindEntityType(typeof(CanonicalScheduleRecord))!
            .FindProperty("CurriculumBlock")!;

        Assert.True(block.IsNullable);
        Assert.Equal(500, block.GetMaxLength());
        Assert.Equal("character varying(500)", block.GetColumnType());
    }

    /// <summary>
    /// The list is required and empty when the source names no department, so
    /// "none stated" is a value rather than a null the reader has to interpret.
    /// </summary>
    [Fact]
    public void DepartmentsAreStoredAsARequiredJsonList()
    {
        IProperty departments = Model.FindEntityType(typeof(CanonicalScheduleRecord))!
            .FindProperty("Departments")!;

        Assert.False(departments.IsNullable);
        Assert.Equal("jsonb", departments.GetColumnType());
        Assert.NotNull(departments.GetValueConverter());
        Assert.NotNull(departments.GetValueComparer());
    }

    /// <summary>
    /// A record is either timed or all-day, and the database says so too, so a
    /// producer that bypasses the domain still cannot store a half-stated shape
    /// (ADR-046).
    /// </summary>
    [Fact]
    public void AnAllDayItemStatesNoTimeAndTheSchemaEnforcesTheShape()
    {
        IEntityType record = Model.FindEntityType(typeof(CanonicalScheduleRecord))!;

        Assert.True(record.FindProperty("StartLocalTime")!.IsNullable);
        Assert.True(record.FindProperty("EndLocalTime")!.IsNullable);
        Assert.False(record.FindProperty("IsAllDay")!.IsNullable);

        // Check constraints are not kept in the read-optimized runtime model.
        IEntityType designTime = CreateContext()
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(CanonicalScheduleRecord))!;
        ICheckConstraint shape = Assert.Single(
            designTime.GetCheckConstraints(),
            constraint => constraint.Name == "ck_canonical_schedule_records_schedule_shape");
        // Nullness is tested explicitly in every branch: a check constraint only
        // fails on FALSE, so a comparison left to return NULL would pass.
        Assert.Contains("IS NOT NULL", shape.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AllDayScheduleItemsHaveTheirOwnMigration()
    {
        Assert.Contains(
            "20260723141930_AddAllDayScheduleItems",
            CreateContext().Database.GetMigrations());
    }

    [Fact]
    public void StaleParseRunRecoveryIsRecordedAndOptional()
    {
        IProperty recovered = Model.FindEntityType(typeof(Domain.Scheduling.Parsing.ParseRun))!
            .FindProperty("LastStaleRecoveryAtUtc")!;

        Assert.True(recovered.IsNullable);
    }

    [Fact]
    public void CanonicalDepartmentHasItsOwnAdditiveMigration()
    {
        Assert.Contains(
            "20260722180000_AddCanonicalDepartment",
            CreateContext().Database.GetMigrations());
    }

    [Fact]
    public void OperationalFreezeIsASingletonWithAConcurrencyToken()
    {
        IEntityType control = Model.FindEntityType(typeof(OperationalFreezeControl))!;

        Assert.Equal("operational_freeze_control", control.GetTableName());
        Assert.Equal(
            ValueGenerated.Never,
            control.FindProperty("Id")!.ValueGenerated);
        Assert.True(control.FindProperty("RowVersion")!.IsConcurrencyToken);
    }

    [Fact]
    public void OperationalFreezeChangesHaveAnAppendOnlyAuditModel()
    {
        IEntityType audit = Model.FindEntityType(typeof(OperationalFreezeAudit))!;

        Assert.Equal(200, audit.FindProperty("ChangedBy")!.GetMaxLength());
        Assert.Equal(2000, audit.FindProperty("Reason")!.GetMaxLength());
        Assert.Equal(100, audit.FindProperty("CorrelationId")!.GetMaxLength());
        Assert.False(audit.FindProperty("ChangedAtUtc")!.IsNullable);
    }

    [Fact]
    public void OperationalFreezeHasItsOwnAdditiveMigration()
    {
        Assert.Contains(
            "20260722220853_AddOperationalFreeze",
            CreateContext().Database.GetMigrations());
    }

    [Fact]
    public void SnapshotPayloadRetentionHasItsOwnMigrationAndExplicitMetadata()
    {
        IEntityType snapshot =
            Model.FindEntityType(typeof(Domain.Scheduling.Ingestion.SourceSnapshot))!;

        Assert.Contains(
            "20260723111607_AddSnapshotPayloadRetention",
            CreateContext().Database.GetMigrations());
        Assert.True(snapshot.FindProperty("Payload")!.IsNullable);
        Assert.Equal(20, snapshot.FindProperty("AcademicYear")!.GetMaxLength());
        Assert.True(snapshot.FindProperty("PayloadPrunedAtUtc")!.IsNullable);
    }

    [Theory]
    [InlineData(typeof(Domain.Scheduling.Ingestion.SourceSnapshot), "Payload")]
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
