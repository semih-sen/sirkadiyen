using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;
using DomainAudienceScope = Sirkadiyen.Domain.SchedulePublication.AudienceScope;
using DomainEventType = Sirkadiyen.Domain.SchedulePublication.ScheduleEventType;
using DomainLanguage = Sirkadiyen.Domain.ScheduleSources.ProgramLanguage;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ScheduleRevisionValidationStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ACleanRevisionIsValidatedAndItsFindingsArePersisted()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, ScheduleRevision revision) = await AddRevisionAsync(
            context,
            [Record(0)]);

        ScheduleRevisionValidationStore store = new(context);
        RevisionValidationInput? input = await store.LoadAsync(revision.Id, Token);

        Assert.NotNull(input);
        Assert.Single(input.Records);
        Assert.False(input.HasPreviousPublishedRevision);

        ScheduleRevisionValidator validator = new(new RevisionValidationOptions());
        RevisionValidationResult result = validator.Validate(input, Now);
        await store.ApplyAsync(revision.Id, result, Now, Token);

        context.ChangeTracker.Clear();
        ScheduleRevision stored = await context.ScheduleRevisions.SingleAsync(
            candidate => candidate.Id == revision.Id,
            Token);
        Assert.Equal(RevisionState.Validated, stored.State);
        Assert.Empty(await FindingsAsync(context, revision.Id));
        Assert.Equal(source.Id, stored.ScheduleSourceId);
    }

    [Fact]
    public async Task AQuarantinedRevisionKeepsItsFindingsAsEvidence()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();

        // A record the parser could not resolve confidently is an error the
        // administrator who reviews this revision has to be able to read back.
        // (A duplicate stable identity cannot be used here: the schema's unique
        // index rejects it before validation ever sees it.)
        (_, ScheduleRevision revision) = await AddRevisionAsync(
            context,
            [Record(0), Record(1, confidence: 0.2m)]);

        ScheduleRevisionValidationStore store = new(context);
        RevisionValidationInput input = (await store.LoadAsync(revision.Id, Token))!;
        RevisionValidationResult result =
            new ScheduleRevisionValidator(new RevisionValidationOptions()).Validate(input, Now);
        await store.ApplyAsync(revision.Id, result, Now, Token);

        context.ChangeTracker.Clear();
        ScheduleRevision stored = await context.ScheduleRevisions.SingleAsync(
            candidate => candidate.Id == revision.Id,
            Token);
        Assert.Equal(RevisionState.ReviewRequired, stored.State);
        Assert.Contains("LowConfidenceRecord", stored.StateReason, StringComparison.Ordinal);

        RevisionValidationFinding finding = Assert.Single(
            await FindingsAsync(context, revision.Id));
        Assert.Equal(RevisionValidationRule.LowConfidenceRecord, finding.Rule);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal(1, finding.AffectedRecordCount);
        Assert.Contains("candidate-1", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlreadyValidatedRevisionsAreNotValidatedTwice()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();

        // A revision that produces a finding, so a duplicated apply would be
        // visible as duplicated evidence rather than as nothing at all.
        (_, ScheduleRevision revision) = await AddRevisionAsync(
            context,
            [Record(0), Record(1, confidence: 0.2m)]);

        ScheduleRevisionValidationStore store = new(context);
        RevisionValidationInput input = (await store.LoadAsync(revision.Id, Token))!;
        RevisionValidationResult result =
            new ScheduleRevisionValidator(new RevisionValidationOptions()).Validate(input, Now);
        await store.ApplyAsync(revision.Id, result, Now, Token);

        // A second cycle must find nothing to do rather than transitioning a
        // revision that has already left Parsed.
        context.ChangeTracker.Clear();
        Assert.Null(await store.LoadAsync(revision.Id, Token));

        await store.ApplyAsync(revision.Id, result, Now, Token);
        context.ChangeTracker.Clear();
        Assert.Single(await FindingsAsync(context, revision.Id));
    }

    [Fact]
    public async Task ValidationRefusesToPublish()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (_, ScheduleRevision revision) = await AddRevisionAsync(context, [Record(0)]);
        ScheduleRevisionValidationStore store = new(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ApplyAsync(
                revision.Id,
                new RevisionValidationResult
                {
                    Outcome = RevisionState.Published,
                    Findings = [],
                    StateReason = "should never happen",
                },
                Now,
                Token));
    }

    [Fact]
    public async Task PendingValidationListsOnlyParsedRevisions()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (_, ScheduleRevision revision) = await AddRevisionAsync(context, [Record(0)]);

        ScheduleRevisionValidationStore store = new(context);
        Assert.Contains(revision.Id, await store.ListPendingValidationAsync(100, Token));

        RevisionValidationInput input = (await store.LoadAsync(revision.Id, Token))!;
        RevisionValidationResult result =
            new ScheduleRevisionValidator(new RevisionValidationOptions()).Validate(input, Now);
        await store.ApplyAsync(revision.Id, result, Now, Token);

        context.ChangeTracker.Clear();
        Assert.DoesNotContain(revision.Id, await store.ListPendingValidationAsync(100, Token));
    }

    [Fact]
    public async Task DeclaredSelectorsSurviveARoundTrip()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();

        Dictionary<string, IReadOnlyList<string>> declared = new()
        {
            ["practiceGroup"] = ["A", "B", "H"],
            ["practiceSubgroup"] = ["A1", "A2"],
        };
        (ScheduleSource source, _) = await AddRevisionAsync(
            context,
            [Record(0)],
            declared);

        context.ChangeTracker.Clear();
        ScheduleSource stored = await context.ScheduleSources.SingleAsync(
            candidate => candidate.Id == source.Id,
            Token);

        Assert.NotNull(stored.SupportedAudienceSelectors);
        Assert.Equal(["A", "B", "H"], stored.SupportedAudienceSelectors["practiceGroup"]);
        Assert.Equal(["A1", "A2"], stored.SupportedAudienceSelectors["practiceSubgroup"]);
    }

    [Fact]
    public async Task ASourceWithoutDeclaredSelectorsStoresNull()
    {
        // "Not declared" and "declared empty" must stay distinguishable through
        // the database, because validation treats them differently.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, _) = await AddRevisionAsync(context, [Record(0)]);

        context.ChangeTracker.Clear();
        ScheduleSource stored = await context.ScheduleSources.SingleAsync(
            candidate => candidate.Id == source.Id,
            Token);

        Assert.Null(stored.SupportedAudienceSelectors);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<List<RevisionValidationFinding>> FindingsAsync(
        SirkadiyenDbContext context,
        Guid revisionId) =>
        await context.RevisionValidationFindings
            .Where(finding => finding.ScheduleRevisionId == revisionId)
            .OrderBy(finding => finding.CreatedAtUtc)
            .ToListAsync(Token);

    private static async Task<(ScheduleSource Source, ScheduleRevision Revision)> AddRevisionAsync(
        SirkadiyenDbContext context,
        IReadOnlyList<RecordSpecification> records,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? supported = null)
    {
        SourceId sourceId = SourceId.Parse($"G1-VALID-{Guid.NewGuid():N}"[..24]);
        ScheduleSource source = new(
            sourceId,
            "Validation test source",
            ScheduleSourceTransport.GoogleSheets,
            ScheduleDocumentFormat.GoogleSheet,
            "https://example.invalid/sheet",
            "grade1_practice_v1",
            "1.0.0",
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            "Europe/Istanbul",
            "spreadsheet-1",
            1,
            supported);

        SourceSnapshot snapshot = new(
            source.Id,
            source.SourceId,
            $"snapshot-{Guid.NewGuid():N}",
            "spreadsheet-1",
            Now,
            $"sha256:{Guid.NewGuid():N}",
            "1.0",
            "{}",
            1,
            1,
            0);

        ParseRun run = new(snapshot.Id, source.ParserProfile, source.ParserProfileVersion, "c1", Now);
        ScheduleRevision revision = new(source.Id, source.SourceId, run.Id, Now);
        revision.SetRecordCount(records.Count);

        context.ScheduleSources.Add(source);
        context.SourceSnapshots.Add(snapshot);
        context.ParseRuns.Add(run);
        context.ScheduleRevisions.Add(revision);
        context.CanonicalScheduleRecords.AddRange(
            records.Select(specification => Materialize(revision.Id, sourceId, specification)));

        await context.SaveChangesAsync(Token);
        return (source, revision);
    }

    private static RecordSpecification Record(int index, decimal confidence = 1.0m) =>
        new(index, confidence);

    private sealed record RecordSpecification(int Index, decimal Confidence);

    private static CanonicalScheduleRecord Materialize(
        Guid revisionId,
        SourceId sourceId,
        RecordSpecification specification)
    {
        IReadOnlyList<AudienceSelector> audience =
        [
            new AudienceSelector { Dimension = "practiceGroup", Value = "A" },
        ];

        return new CanonicalScheduleRecord(
            revisionId,
            sourceId,
            $"candidate-{specification.Index}",
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            DomainEventType.Practice,
            DomainAudienceScope.SelectedGroups,
            JsonSerializer.Serialize(audience, ContractJson.CreateOptions()),
            $"Lesson {specification.Index}",
            null,
            new DateOnly(2025, 10, 3).AddDays(specification.Index),
            new TimeOnly(9, 0),
            new TimeOnly(10, 50),
            "Europe/Istanbul",
            $"identity-{specification.Index}",
            $"sha256:content-{specification.Index}",
            specification.Confidence,
            "[]");
    }
}
