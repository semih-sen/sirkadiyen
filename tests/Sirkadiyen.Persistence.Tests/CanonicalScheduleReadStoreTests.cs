using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;
using DomainAudienceScope = Sirkadiyen.Domain.SchedulePublication.AudienceScope;
using DomainEventType = Sirkadiyen.Domain.SchedulePublication.ScheduleEventType;
using DomainLanguage = Sirkadiyen.Domain.ScheduleSources.ProgramLanguage;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CanonicalScheduleReadStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OnlyTheCurrentPublishedRevisionsRecordsAreReturned()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        string publishedId = $"published-{Guid.NewGuid():N}";
        string unpublishedId = $"unpublished-{Guid.NewGuid():N}";

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();

        ScheduleSource published = await ScheduleDiffScenario.AddSourceAsync(context);
        await ScheduleDiffScenario.PublishAsync(context, published, Now, [publishedId]);

        // A validated but never-published revision holds records that must stay invisible.
        ScheduleSource pendingSource = await ScheduleDiffScenario.AddSourceAsync(context);
        await ScheduleDiffScenario.AddRevisionAsync(context, pendingSource, Now, [unpublishedId]);

        IReadOnlyList<string> identities = await ReadIdentitiesAsync(
            context,
            1,
            DomainLanguage.Turkish);

        Assert.Contains(publishedId, identities);
        Assert.DoesNotContain(unpublishedId, identities);
    }

    [Fact]
    public async Task CancelledRecordsAreExcluded()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        string scheduledId = $"scheduled-{Guid.NewGuid():N}";
        string cancelledId = $"cancelled-{Guid.NewGuid():N}";

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();

        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        ScheduleRevision revision = await ScheduleDiffScenario.PublishAsync(
            context,
            source,
            Now,
            [scheduledId]);

        // A cancelled lesson lives in the published revision but is not part of the live
        // schedule a student should see during initial sync.
        context.CanonicalScheduleRecords.Add(Cancelled(revision.Id, source.SourceId, cancelledId));
        await context.SaveChangesAsync(Token);

        IReadOnlyList<string> identities = await ReadIdentitiesAsync(
            context,
            1,
            DomainLanguage.Turkish);

        Assert.Contains(scheduledId, identities);
        Assert.DoesNotContain(cancelledId, identities);
    }

    [Fact]
    public async Task RecordsForAnotherProgramAreNotReturned()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        string identity = $"mine-{Guid.NewGuid():N}";

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        await ScheduleDiffScenario.PublishAsync(context, source, Now, [identity]);

        // The seeded records are first-year Turkish, so neither another language nor another
        // class year sees them.
        Assert.DoesNotContain(
            identity,
            await ReadIdentitiesAsync(context, 1, DomainLanguage.English));
        Assert.DoesNotContain(
            identity,
            await ReadIdentitiesAsync(context, 2, DomainLanguage.Turkish));
    }

    private static async Task<IReadOnlyList<string>> ReadIdentitiesAsync(
        SirkadiyenDbContext context,
        int classYear,
        DomainLanguage programLanguage)
    {
        IReadOnlyList<CanonicalScheduleRecord> records = await new CanonicalScheduleReadStore(context)
            .ListCurrentPublishedRecordsAsync("2025-2026", classYear, programLanguage, Token);
        return [.. records.Select(record => record.StableIdentity)];
    }

    private static CanonicalScheduleRecord Cancelled(
        Guid revisionId,
        SourceId sourceId,
        string identity) =>
        new(
            revisionId,
            sourceId,
            $"candidate-{identity}",
            CanonicalRecordStatus.Cancelled,
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            DomainEventType.Theory,
            DomainAudienceScope.AllStudentsInProgram,
            "[]",
            $"Lesson {identity}",
            null,
            new DateOnly(2025, 10, 3),
            new TimeOnly(9, 0),
            new TimeOnly(10, 50),
            isAllDay: false,
            "Europe/Istanbul",
            identity,
            $"sha256:{identity}",
            1.0m,
            "[]");

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
