using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ScheduleSourceStoreTests(PostgresFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheConfiguredCatalogSeedsTheDatabase()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        IReadOnlyList<ScheduleSource> configured = await LoadCatalogAsync();

        await using SirkadiyenDbContext context = fixture.CreateContext();
        int changed = await new ScheduleSourceStore(context).UpsertAsync(configured, Token);

        Assert.Equal(configured.Count, changed);

        ScheduleSource stored = await context.ScheduleSources
            .SingleAsync(source => source.SourceId == SourceId.Parse("G1-TR-ANNUAL"), Token);

        // The source context the parser needs is configuration, so it has to
        // survive the round trip intact.
        Assert.Equal("2025-2026", stored.AcademicYear);
        Assert.Equal(1, stored.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, stored.ProgramLanguage);
        Assert.Equal("Europe/Istanbul", stored.TimeZoneId);
        Assert.Equal("grade1_yearly_v1", stored.ParserProfile);
    }

    [Fact]
    public async Task ReseedingAnUnchangedCatalogChangesNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        IReadOnlyList<ScheduleSource> configured = await LoadCatalogAsync();

        await using (SirkadiyenDbContext first = fixture.CreateContext())
        {
            await new ScheduleSourceStore(first).UpsertAsync(configured, Token);
        }

        await using SirkadiyenDbContext second = fixture.CreateContext();
        int changed = await new ScheduleSourceStore(second).UpsertAsync(
            await LoadCatalogAsync(),
            Token);

        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task PollingHistorySurvivesAReseed()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-RESEED");
        DateTimeOffset polledAt = new(2026, 7, 21, 9, 0, 0, TimeSpan.Zero);

        await using (SirkadiyenDbContext seed = fixture.CreateContext())
        {
            ScheduleSource source = Definition(sourceId, "Original name");
            source.RecordPolled(polledAt, changed: true);
            seed.ScheduleSources.Add(source);
            await seed.SaveChangesAsync(Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        await new ScheduleSourceStore(context).UpsertAsync(
            [Definition(sourceId, "Renamed source")],
            Token);

        ScheduleSource stored = await context.ScheduleSources
            .SingleAsync(source => source.SourceId == sourceId, Token);

        // Configuration is owned by the catalog; what the worker observed is
        // owned by the row and must not be reset by a redeploy.
        Assert.Equal("Renamed source", stored.DisplayName);
        Assert.Equal(polledAt, stored.LastPolledAtUtc);
        Assert.Equal(polledAt, stored.LastChangedAtUtc);
    }

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

    private static async Task<IReadOnlyList<ScheduleSource>> LoadCatalogAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "schedule-sources.json");
        ScheduleSourceCatalog catalog = await new ScheduleSourceCatalogLoader()
            .LoadAsync(path, Token);

        return [.. catalog.Sources.Select(static source => source.ToScheduleSource())];
    }
}
