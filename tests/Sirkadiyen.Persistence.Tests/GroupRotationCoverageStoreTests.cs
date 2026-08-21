using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;
using DomainLanguage = Sirkadiyen.Domain.Scheduling.Sources.ProgramLanguage;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// The dates a group rotation's owners have published decide where the Grade 2
/// annual program keeps deferring its dissection rows and where it publishes all
/// three hours itself (ADR-126).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GroupRotationCoverageStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The date every scenario record is scheduled on.</summary>
    private static readonly DateOnly ScenarioDate = new(2025, 10, 3);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePublishedDatesOfTheNamedOwnersAreReturned()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource owner = await ScheduleDiffScenario.AddSourceAsync(context);
        await ScheduleDiffScenario.PublishAsync(
            context,
            owner,
            Now,
            [$"covered-{Guid.NewGuid():N}"]);

        IReadOnlyList<DateOnly> covered = await ReadAsync(context, [owner.SourceId]);

        Assert.Equal([ScenarioDate], covered);
    }

    [Fact]
    public async Task ASourceThatIsNotNamedAsAnOwnerCoversNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource other = await ScheduleDiffScenario.AddSourceAsync(context);
        await ScheduleDiffScenario.PublishAsync(context, other, Now, [$"other-{Guid.NewGuid():N}"]);

        ScheduleSource owner = await ScheduleDiffScenario.AddSourceAsync(context);

        // The owner has published nothing of its own, so the day the *other*
        // source states must not silence the fallback.
        Assert.Empty(await ReadAsync(context, [owner.SourceId]));
    }

    [Fact]
    public async Task AnUnpublishedRevisionOfTheOwnerCoversNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource owner = await ScheduleDiffScenario.AddSourceAsync(context);

        // Parsed and validated is not published: it says nothing to a student
        // yet, so removing the fallback for it would leave the day empty.
        await ScheduleDiffScenario.AddRevisionAsync(
            context,
            owner,
            Now,
            [$"pending-{Guid.NewGuid():N}"]);

        Assert.Empty(await ReadAsync(context, [owner.SourceId]));
    }

    [Fact]
    public async Task CoverageIsReadForTheAskingSourcesOwnProgram()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource owner = await ScheduleDiffScenario.AddSourceAsync(context);
        await ScheduleDiffScenario.PublishAsync(context, owner, Now, [$"g1-{Guid.NewGuid():N}"]);

        // The seeded records are 2025-2026 first-year Turkish. An owner still
        // pointing at another year or class covers nothing for the asking source,
        // which is exactly the state a rollover leaves behind (ADR-115).
        Assert.Empty(await ReadAsync(context, [owner.SourceId], academicYear: "2026-2027"));
        Assert.Empty(await ReadAsync(context, [owner.SourceId], classYear: 2));
        Assert.Empty(
            await ReadAsync(context, [owner.SourceId], language: DomainLanguage.English));
    }

    [Fact]
    public async Task NamingNoOwnerAsksTheDatabaseNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();

        Assert.Empty(await ReadAsync(context, []));
    }

    private static async Task<IReadOnlyList<DateOnly>> ReadAsync(
        SirkadiyenDbContext context,
        IReadOnlyCollection<SourceId> owners,
        string academicYear = "2025-2026",
        int classYear = 1,
        DomainLanguage language = DomainLanguage.Turkish) =>
        await new GroupRotationCoverageStore(context).ListPublishedDatesAsync(
            owners,
            academicYear,
            classYear,
            language,
            Token);
}
