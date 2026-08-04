using Sirkadiyen.Application.Administration;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
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
    public async Task UnknownSourceDetailIsNull()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Null(await new SourceStatusReadStore(context).FindAsync("G9-NONE-XYZ", Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
