using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Scheduling.Access;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class UserScheduleReadStoreTests(PostgresFixture fixture)
{
    // The scenario helper seeds records on 2025-10-03.
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly RecordDate = new(2025, 10, 3);

    [Fact]
    public async Task UpcomingReturnsMappedEventsInsideTheWindowOnly()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("schedule-upcoming");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        (SourceId sourceId, string identity, Guid recordId) = await PublishAndMapAsync(context, user);

        UserScheduleReadStore store = new(context);
        IReadOnlyList<UserScheduleEventView> inWindow = await store.ListUpcomingAsync(
            user.UserId,
            RecordDate.AddDays(-2),
            RecordDate.AddDays(2),
            100,
            Token);

        UserScheduleEventView only = Assert.Single(inWindow);
        Assert.Equal(identity, only.StableIdentity);
        Assert.Equal($"Lesson {identity}", only.Title);
        Assert.Equal(RecordDate, only.LocalDate);
        Assert.False(only.IsAllDay);

        // A window that excludes the record's date returns nothing.
        Assert.Empty(await store.ListUpcomingAsync(
            user.UserId,
            RecordDate.AddDays(10),
            RecordDate.AddDays(20),
            100,
            Token));

        _ = sourceId;
        _ = recordId;
    }

    [Fact]
    public async Task ChangesReportsCreationThenUpdate()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("schedule-changes");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        (SourceId _, string identity, Guid recordId) = await PublishAndMapAsync(context, user);

        UserScheduleReadStore store = new(context);
        UserScheduleChangeView created = Assert.Single(
            await store.ListRecentChangesAsync(user.UserId, 20, Token));
        Assert.Equal(UserScheduleChangeKind.Created, created.Kind);
        Assert.Equal(identity, created.StableIdentity);

        DateTimeOffset patchedAt = Now.AddDays(1);
        await new UserCalendarEventMappingStore(context).UpdateContentAsync(
            user.UserId,
            identity,
            recordId,
            $"sha256:{identity}-v2",
            patchedAt,
            Token);

        UserScheduleChangeView updated = Assert.Single(
            await store.ListRecentChangesAsync(user.UserId, 20, Token));
        Assert.Equal(UserScheduleChangeKind.Updated, updated.Kind);
        Assert.Equal(patchedAt, updated.ChangedAtUtc);
    }

    private static async Task<(SourceId SourceId, string Identity, Guid RecordId)> PublishAndMapAsync(
        SirkadiyenDbContext context,
        UserSession user)
    {
        ScheduleSource source = await ScheduleDiffScenario.AddSourceAsync(context);
        string identity = $"lesson-{Guid.NewGuid():N}";
        await ScheduleDiffScenario.PublishAsync(context, source, Now, [identity]);

        Guid recordId = await context.CanonicalScheduleRecords
            .AsNoTracking()
            .Where(record => record.StableIdentity == identity)
            .Select(record => record.Id)
            .SingleAsync(Token);

        await new UserCalendarEventMappingStore(context).AddAsync(
            UserCalendarEventMapping.Create(
                user.UserId,
                identity,
                source.SourceId,
                recordId,
                "sirkadiyen@group.calendar.google.com",
                $"event-{identity}",
                $"sha256:{identity}",
                Now),
            Token);

        return (source.SourceId, identity, recordId);
    }

    private async Task<UserSession> CreateUserAsync(string prefix)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        string nonce = Guid.NewGuid().ToString("N");
        return await new UserStore(context).SignInWithGoogleAsync(
            new GoogleIdentity
            {
                Subject = $"{prefix}-{nonce}",
                Email = $"{prefix}-{nonce}@example.com",
                EmailVerified = true,
            },
            UserRole.User,
            Now,
            Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
