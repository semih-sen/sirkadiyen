using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;
using DomainAudienceScope = Sirkadiyen.Domain.Scheduling.Publication.AudienceScope;
using DomainEventType = Sirkadiyen.Domain.Scheduling.Publication.ScheduleEventType;
using DomainLanguage = Sirkadiyen.Domain.Scheduling.Sources.ProgramLanguage;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Every store that opens its own transaction, exercised against the context
/// configuration the worker and the API actually use.
/// </summary>
/// <remarks>
/// The hosts enable retry on transient failures. Saving inside a hand-rolled
/// transaction under a retrying execution strategy throws, so these paths used to
/// work in the test suite and fail on the first real poll. The plain fixture
/// context cannot catch that, which is the entire reason these tests exist
/// separately from the ones that cover the same stores' behaviour.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RetriableTransactionTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StoringASnapshotSurvivesTheRetryingExecutionStrategy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await AddSourceAsync(context);

        SourceSnapshotStore store = new(context);
        StoreSnapshotResult result = await store.StoreIfChangedAsync(
            source.SourceId,
            Snapshot(source),
            Token);

        Assert.Equal(StoreSnapshotOutcome.Stored, result.Outcome);
    }

    [Fact]
    public async Task TheUnchangedShortCircuitSurvivesTheRetryingExecutionStrategy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await AddSourceAsync(context);
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(source);

        SourceSnapshotStore store = new(context);
        await store.StoreIfChangedAsync(source.SourceId, snapshot, Token);
        StoreSnapshotResult second = await store.StoreIfChangedAsync(
            source.SourceId,
            snapshot,
            Token);

        Assert.Equal(StoreSnapshotOutcome.Unchanged, second.Outcome);
    }

    [Fact]
    public async Task ValidationSurvivesTheRetryingExecutionStrategy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await AddSourceAsync(context);
        ScheduleRevision revision = await AddParsedRevisionAsync(context, source);

        ScheduleRevisionValidationStore store = new(context);
        RevisionValidationInput input = (await store.LoadAsync(revision.Id, Token))!;
        RevisionValidationResult result =
            new ScheduleRevisionValidator(new RevisionValidationOptions()).Validate(input, Now);

        await store.ApplyAsync(revision.Id, result, Now, Token);

        context.ChangeTracker.Clear();
        ScheduleRevision stored = await context.ScheduleRevisions.SingleAsync(
            candidate => candidate.Id == revision.Id,
            Token);
        Assert.Equal(RevisionState.Validated, stored.State);
    }

    [Fact]
    public async Task DepartmentColorMutationSurvivesTheRetryingExecutionStrategy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        DepartmentColorStore store = new(context);
        string departmentKey = $"retry-test-{Guid.NewGuid():N}";
        string correlationId = $"test-{Guid.NewGuid():N}";

        bool changed = await store.SetAdminDefaultAsync(
            departmentKey,
            "#123456",
            "integration-test",
            "Prove the color transaction runs inside the retry strategy.",
            correlationId,
            Now,
            Token);
        bool repeated = await store.SetAdminDefaultAsync(
            departmentKey,
            "#123456",
            "integration-test",
            "Prove the no-change path also runs inside the retry strategy.",
            $"{correlationId}-repeat",
            Now.AddSeconds(1),
            Token);

        Assert.True(changed);
        Assert.False(repeated);
        Assert.Equal(
            "#123456",
            await context.DepartmentColorSettings
                .Where(item => item.DepartmentKey == departmentKey)
                .Select(item => item.BackgroundColor)
                .SingleAsync(Token));
        Assert.Single(
            await context.DepartmentColorAudits
                .Where(item => item.CorrelationId == correlationId)
                .ToListAsync(Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<ScheduleSource> AddSourceAsync(SirkadiyenDbContext context)
    {
        ScheduleSource source = new(
            SourceId.Parse($"G1-RETRY-{Guid.NewGuid():N}"[..24]),
            "Retry test source",
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
            1);

        context.ScheduleSources.Add(source);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();
        return source;
    }

    private static NormalizedSpreadsheetSnapshot Snapshot(ScheduleSource source) => new()
    {
        ContractVersion = "1.0",
        SourceId = source.SourceId.Value,
        SnapshotId = $"snapshot-{Guid.NewGuid():N}",
        SpreadsheetId = "spreadsheet-1",
        AcquiredAtUtc = Now,
        ContentHash = $"sha256:{Guid.NewGuid():N}",
        ContentHashAlgorithm = "sha256",
        Worksheets = [],
        Diagnostics = [],
    };

    private static async Task<ScheduleRevision> AddParsedRevisionAsync(
        SirkadiyenDbContext context,
        ScheduleSource source)
    {
        SourceSnapshot snapshot = new(
            source.Id,
            source.SourceId,
            $"snapshot-{Guid.NewGuid():N}",
            "spreadsheet-1",
            source.AcademicYear,
            Now,
            $"sha256:{Guid.NewGuid():N}",
            "1.0",
            "{}",
            1,
            1,
            0);

        ParseRun run = new(
            snapshot.Id,
            source.ParserProfile,
            source.ParserProfileVersion,
            $"c-{Guid.NewGuid():N}",
            Now);

        ScheduleRevision revision = new(source.Id, source.SourceId, run.Id, Now);

        IReadOnlyList<AudienceSelector> audience =
        [
            new AudienceSelector { Dimension = "practiceGroup", Value = "A" },
        ];

        CanonicalScheduleRecord record = new(
            revision.Id,
            source.SourceId,
            "candidate-0",
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            DomainEventType.Practice,
            DomainAudienceScope.SelectedGroups,
            JsonSerializer.Serialize(audience, ContractJson.CreateOptions()),
            "Lesson 0",
            null,
            new DateOnly(2025, 10, 3),
            new TimeOnly(9, 0),
            new TimeOnly(10, 50),
            isAllDay: false,
            "Europe/Istanbul",
            "identity-0",
            "sha256:content-0",
            1.0m,
            "[]");
        revision.SetRecordSet([record]);

        context.SourceSnapshots.Add(snapshot);
        context.ParseRuns.Add(run);
        context.ScheduleRevisions.Add(revision);
        context.CanonicalScheduleRecords.Add(record);

        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();
        return revision;
    }
}
