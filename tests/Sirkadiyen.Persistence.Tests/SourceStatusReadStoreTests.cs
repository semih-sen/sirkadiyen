using Sirkadiyen.Application.Administration;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Administration.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SourceStatusReadStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DetailReportsLatestRevisionAndRecentSnapshots()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        string identity = $"lesson-{Guid.NewGuid():N}";
        await ScheduleDiffScenario.PublishAsync(context, source, Now, [identity]);

        SourceStatusDetail? detail = await new SourceStatusReadStore(context)
            .FindAsync(source.SourceId.Value, Token);

        Assert.NotNull(detail);
        Assert.Equal(source.SourceId.Value, detail!.Summary.SourceId);
        Assert.Equal(RevisionState.Published, detail.Summary.LatestRevisionState);
        Assert.NotNull(detail.Summary.LatestRevisionId);
        Assert.Equal(source.ParserProfile, detail.ParserProfile);

        SourceSnapshotSummary snapshot = Assert.Single(detail.RecentSnapshots);
        Assert.True(snapshot.HasPayload);
    }

    [Fact]
    public async Task ListIncludesTheSeededSource()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);

        IReadOnlyList<SourceStatusListItem> items =
            await new SourceStatusReadStore(context).ListAsync(Token);

        Assert.Contains(items, item => item.SourceId == source.SourceId.Value);
    }

    [Fact]
    public async Task FailedParseRunSurfacesItsFailureReason()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);

        // A failed run stores no parser response, so it carries no warnings: the reason is the only
        // record of why the parse could not finish, and the status view is where an operator reads it.
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
        run.Fail(Now, "InvalidDataException: Candidate 'S1!R4C3' contradicts its configured source context.");

        context.SourceSnapshots.Add(snapshot);
        context.ParseRuns.Add(run);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();

        SourceStatusListItem item = Assert.Single(
            await new SourceStatusReadStore(context).ListAsync(Token),
            candidate => candidate.SourceId == source.SourceId.Value);

        Assert.Equal("Failed", item.LatestParseRunStatus?.ToString());
        Assert.Equal(
            "InvalidDataException: Candidate 'S1!R4C3' contradicts its configured source context.",
            item.LatestParseFailureReason);
        Assert.Equal(0, item.LatestParseWarningCount);
        Assert.Equal(0, item.LatestParseErrorCount);
    }

    [Fact]
    public async Task UnknownSourceDetailIsNull()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Null(await new SourceStatusReadStore(context).FindAsync("G9-NONE-XYZ", Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
