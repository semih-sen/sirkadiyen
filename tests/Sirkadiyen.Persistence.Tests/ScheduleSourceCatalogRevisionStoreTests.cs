using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Covers the one transaction that makes an administrative catalog edit take effect (ADR-114):
/// the revision, the sources it configures and the sources it retires all commit together, or
/// none of them do.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ScheduleSourceCatalogRevisionStoreTests(PostgresFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ACommitRecordsTheRevisionAndAppliesItsSources()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-CATALOG-APPLY");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        int changed = await new ScheduleSourceCatalogRevisionStore(context).CommitAsync(
            Commit("{\"catalogVersion\":\"1.0\"}\n", [Definition(sourceId, "Applied")]),
            Token);

        Assert.Equal(1, changed);
        Assert.Equal(
            "Applied",
            (await context.ScheduleSources.SingleAsync(
                source => source.SourceId == sourceId,
                Token)).DisplayName);
        Assert.Single(
            await context.ScheduleSourceCatalogRevisions
                .Where(revision => revision.Kind == ScheduleSourceCatalogRevisionKind.Edit)
                .ToListAsync(Token));
    }

    [Fact]
    public async Task TheFirstCommitAlsoStoresTheDocumentThatWasThereBefore()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSourceCatalogRevisionStore store = new(context);
        await store.CommitAsync(
            Commit("{\"catalogVersion\":\"1.0\"}\n", [Definition(SourceId.Parse("G9-BASE-1"), "A")]),
            Token);
        await store.CommitAsync(
            Commit("{\"catalogVersion\":\"1.0\",\"sources\":[]}\n", [Definition(SourceId.Parse("G9-BASE-2"), "B")]),
            Token);

        List<ScheduleSourceCatalogRevision> baselines = await context.ScheduleSourceCatalogRevisions
            .Where(revision => revision.Kind == ScheduleSourceCatalogRevisionKind.Baseline)
            .ToListAsync(Token);

        // Exactly one, from the first commit. The second edit's "previous content" is already a
        // stored revision, so a second baseline would be a duplicate of it.
        ScheduleSourceCatalogRevision baseline = Assert.Single(baselines);
        Assert.Equal("the document that was on disk\n", baseline.Content);
        Assert.Null(baseline.ActorUserId);
    }

    [Fact]
    public async Task ARetiredSourceKeepsItsRowAndLosesOnlyItsPolling()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId retired = SourceId.Parse("G9-CATALOG-RETIRED");
        SourceId kept = SourceId.Parse("G9-CATALOG-KEPT");

        await using (SirkadiyenDbContext seed = fixture.CreateContext())
        {
            seed.ScheduleSources.Add(Definition(retired, "Dropped from the catalog"));
            await seed.SaveChangesAsync(Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        await new ScheduleSourceCatalogRevisionStore(context).CommitAsync(
            Commit(
                "{\"catalogVersion\":\"1.0\"}\n",
                [Definition(kept, "Still configured")],
                pollingDisabled: [retired]),
            Token);

        ScheduleSource stored = await context.ScheduleSources
            .SingleAsync(source => source.SourceId == retired, Token);

        // Absence from a configuration file is not a publication decision: the row, its snapshots
        // and everything it published stay exactly where they are (AI_GUIDELINE §13).
        Assert.False(stored.IsPollingEnabled);
        Assert.Equal("Dropped from the catalog", stored.DisplayName);
    }

    [Fact]
    public async Task TheHistoryMarksTheRevisionMatchingTheFileAsCurrent()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        string content = "{\"catalogVersion\":\"1.0\",\"sources\":[]}\n";

        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSourceCatalogRevisionStore store = new(context);
        await store.CommitAsync(
            Commit(content, [Definition(SourceId.Parse("G9-CURRENT"), "Current")]),
            Token);

        IReadOnlyList<ScheduleSourceCatalogRevisionSummary> history = await store.ListAsync(
            50,
            ScheduleSourceCatalogPlanner.Hash(content),
            Token);

        // Compared against the file, not against the newest row: a document changed outside the
        // panel must not be presented as the last confirmed revision.
        Assert.Contains(history, revision => revision.IsCurrent && revision.Kind == "Edit");
        Assert.DoesNotContain(history, revision => revision.IsCurrent && revision.Kind == "Baseline");
    }

    private static ScheduleSourceCatalogCommit Commit(
        string content,
        IReadOnlyCollection<ScheduleSource> sources,
        IReadOnlyCollection<SourceId>? pollingDisabled = null) => new()
        {
            Revision = ScheduleSourceCatalogRevision.Edit(
                Now,
                content,
                ScheduleSourceCatalogPlanner.Hash(content),
                previousContentHash: null,
                sources.Count,
                Guid.CreateVersion7(),
                "admin@example.com",
                "Test",
                correlationId: null,
                changeSummary: "{\"added\":[]}"),
            Baseline = new ScheduleSourceCatalogBaselineDraft
            {
                Content = "the document that was on disk\n",
                ContentHash = ScheduleSourceCatalogPlanner.Hash("the document that was on disk\n"),
                SourceCount = 0,
                RecordedAtUtc = Now.AddMinutes(-1),
            },
            Sources = sources,
            PollingDisabled = pollingDisabled ?? [],
        };

    private static ScheduleSource Definition(SourceId sourceId, string displayName) => new(
        sourceId,
        displayName,
        ScheduleSourceTransport.GoogleSheets,
        ScheduleDocumentFormat.GoogleSheet,
        "https://example.invalid/sheet",
        "grade1_yearly_v1",
        "1.0.0",
        "2025-2026",
        1,
        ProgramLanguage.Turkish,
        "Europe/Istanbul");
}
