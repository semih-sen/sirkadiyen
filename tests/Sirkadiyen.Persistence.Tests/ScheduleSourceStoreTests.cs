using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
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

        // Whether a source publishes at all is configuration too, and it decides whether an empty
        // revision is an alarm (ADR-156).
        Assert.True(stored.PublishesSchedule);
        Assert.False(
            (await context.ScheduleSources.SingleAsync(
                source => source.SourceId == SourceId.Parse("SHARED-AMPHI"),
                Token)).PublishesSchedule);
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

    [Fact]
    public async Task ADeclaredCompanionReachesARowThatWasSeededWithoutOne()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-ANNUAL");
        SourceId companionId = SourceId.Parse("G9-BEDSIDE");

        await using (SirkadiyenDbContext seed = fixture.CreateContext())
        {
            seed.ScheduleSources.Add(Definition(sourceId, "Annual"));
            await seed.SaveChangesAsync(Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        await new ScheduleSourceStore(context).UpsertAsync(
            [Definition(sourceId, "Annual", companions: [companionId])],
            Token);

        ScheduleSource stored = await context.ScheduleSources
            .SingleAsync(source => source.SourceId == sourceId, Token);

        // A companion declared after the row was seeded must reach it. Nothing
        // downstream reports its absence: the parse succeeds, the schedule is
        // published in full, and only the topic the companion states is
        // silently missing from every event (ADR-112).
        Assert.Equal([companionId], stored.CompanionSourceIds);
    }

    /// <summary>
    /// A source the catalog stopped declaring is retired, not deleted (ADR-155).
    /// </summary>
    /// <remarks>
    /// Applied from the whole catalog on every start, so a source dropped by a release that ran
    /// months ago is retired the next time the worker comes up rather than sitting in the panel's
    /// operational list for good — which is what `G2-VERTICAL-SPRING` and `G2-VERTICAL-AUTUMN`
    /// were doing.
    /// </remarks>
    [Fact]
    public async Task ASourceTheCatalogNoLongerDeclaresIsRetiredWithoutBeingDeleted()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-DROPPED");
        DateTimeOffset appliedAt = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

        await using (SirkadiyenDbContext seed = fixture.CreateContext())
        {
            ScheduleSource source = Definition(sourceId, "Dropped source");
            source.RecordPolled(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), changed: true);
            source.RecordPollFailure(
                new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero),
                "The Drive file is in the trash.");
            seed.ScheduleSources.Add(source);
            await seed.SaveChangesAsync(Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSourceCatalogApplication applied = await new ScheduleSourceStore(context)
            .ApplyCatalogAsync(await LoadCatalogAsync(), appliedAt, Token);

        Assert.Contains(sourceId, applied.Retired);

        ScheduleSource stored = await context.ScheduleSources
            .SingleAsync(source => source.SourceId == sourceId, Token);

        Assert.Equal(appliedAt, stored.RetiredAtUtc);
        Assert.False(stored.IsPollingEnabled);

        // The evidence of what it did while it was configured is untouched.
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), stored.LastPolledAtUtc);

        // But its last failure is cleared: nothing will poll it again, so it can never resolve,
        // and a permanent alarm is one nobody reads.
        Assert.Null(stored.LastPollFailureAtUtc);
        Assert.Null(stored.LastPollFailureReason);
    }

    [Fact]
    public async Task RetiringIsIdempotentAndTheCatalogCanBringASourceBack()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-RETURNED");
        DateTimeOffset retiredAt = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

        await using (SirkadiyenDbContext seed = fixture.CreateContext())
        {
            seed.ScheduleSources.Add(Definition(sourceId, "Returning source"));
            await seed.SaveChangesAsync(Token);
        }

        IReadOnlyList<ScheduleSource> catalog = await LoadCatalogAsync();

        await using (SirkadiyenDbContext first = fixture.CreateContext())
        {
            await new ScheduleSourceStore(first).ApplyCatalogAsync(catalog, retiredAt, Token);
        }

        await using (SirkadiyenDbContext second = fixture.CreateContext())
        {
            // A second application must not rewrite the retirement date, or "since when has
            // nobody been reading this" stops being answerable.
            ScheduleSourceCatalogApplication again = await new ScheduleSourceStore(second)
                .ApplyCatalogAsync(catalog, retiredAt.AddDays(3), Token);

            Assert.DoesNotContain(sourceId, again.Retired);
            Assert.Equal(
                retiredAt,
                (await second.ScheduleSources.SingleAsync(
                    source => source.SourceId == sourceId,
                    Token)).RetiredAtUtc);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSourceCatalogApplication reinstating = await new ScheduleSourceStore(context)
            .ApplyCatalogAsync(
                [.. catalog, Definition(sourceId, "Returning source")],
                retiredAt.AddDays(4),
                Token);

        Assert.Contains(sourceId, reinstating.Reinstated);

        ScheduleSource stored = await context.ScheduleSources
            .SingleAsync(source => source.SourceId == sourceId, Token);

        Assert.Null(stored.RetiredAtUtc);
        Assert.True(stored.IsPollingEnabled);
    }

    /// <summary>
    /// A completed cycle clears a recorded failure even when nothing was acquired (ADR-155).
    /// </summary>
    /// <remarks>
    /// An administratively uploaded source acquires nothing during a poll, so the acquisition path
    /// — until this existed, the only writer of a successful poll — never ran for it. One failed
    /// cycle therefore stayed on the row for good, and the panel went on saying the document could
    /// not be acquired while the source was parsed and published successfully every cycle.
    /// </remarks>
    [Fact]
    public async Task ACompletedCycleClearsAFailureForASourceThatAcquiresNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = SourceId.Parse("G9-UPLOADED");
        DateTimeOffset uploadedAt = new(2026, 9, 7, 14, 9, 0, TimeSpan.Zero);
        DateTimeOffset polledAt = new(2026, 9, 8, 10, 1, 0, TimeSpan.Zero);

        await using (SirkadiyenDbContext seed = fixture.CreateContext())
        {
            ScheduleSource source = Definition(sourceId, "Uploaded source");
            source.RecordPolled(uploadedAt, changed: true);
            source.RecordPollFailure(
                uploadedAt.AddHours(2),
                "The parser service refused the request.");
            seed.ScheduleSources.Add(source);
            await seed.SaveChangesAsync(Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        await new ScheduleSourceStore(context).RecordPollCompletedAsync(sourceId, polledAt, Token);

        ScheduleSource stored = await context.ScheduleSources
            .SingleAsync(source => source.SourceId == sourceId, Token);

        Assert.Null(stored.LastPollFailureAtUtc);
        Assert.Null(stored.LastPollFailureReason);
        Assert.Equal(polledAt, stored.LastPolledAtUtc);

        // The cycle acquired nothing, so what the source last changed is still the upload.
        Assert.Equal(uploadedAt, stored.LastChangedAtUtc);
    }

    private static ScheduleSource Definition(
        SourceId sourceId,
        string displayName,
        IReadOnlyList<SourceId>? companions = null) => new(
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
        "Europe/Istanbul",
        companionSourceIds: companions);

    private static async Task<IReadOnlyList<ScheduleSource>> LoadCatalogAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "schedule-sources.json");
        ScheduleSourceCatalog catalog = await new ScheduleSourceCatalogLoader()
            .LoadAsync(path, Token);

        return [.. catalog.Sources.Select(static source => source.ToScheduleSource())];
    }
}
