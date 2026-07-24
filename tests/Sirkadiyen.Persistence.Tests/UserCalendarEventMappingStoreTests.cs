using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class UserCalendarEventMappingStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly SourceId Source = SourceId.Parse("G1-TR-ANNUAL");

    [Fact]
    public async Task AMappingIsInsertedAndCountedForItsUser()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("mapping-insert");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);

        CalendarEventMappingAddOutcome outcome = await store.AddAsync(
            Mapping(user.UserId, "identity-1"),
            Token);

        Assert.Equal(CalendarEventMappingAddOutcome.Added, outcome);
        Assert.Equal(1, await store.CountForUserAsync(user.UserId, Token));
        Assert.Contains(
            "identity-1",
            await store.ListStableIdentitiesForUserAsync(user.UserId, Token));
    }

    [Fact]
    public async Task WritingTheSameLessonTwiceIsReportedAsAlreadyPresent()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("mapping-duplicate");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);

        Assert.Equal(
            CalendarEventMappingAddOutcome.Added,
            await store.AddAsync(Mapping(user.UserId, "identity-dup"), Token));

        // The (UserId, StableIdentity) index is the idempotency guarantee: a resumed pass that
        // re-writes the same lesson is a no-op, not a duplicate row (ADR-058).
        Assert.Equal(
            CalendarEventMappingAddOutcome.AlreadyPresent,
            await store.AddAsync(Mapping(user.UserId, "identity-dup"), Token));
        Assert.Equal(1, await store.CountForUserAsync(user.UserId, Token));
    }

    [Fact]
    public async Task IdentitiesAreScopedToTheirOwnUser()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession owner = await CreateUserAsync("mapping-owner");
        UserSession other = await CreateUserAsync("mapping-other");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);
        await store.AddAsync(Mapping(owner.UserId, "shared-identity"), Token);

        Assert.Empty(await store.ListStableIdentitiesForUserAsync(other.UserId, Token));
        Assert.Equal(0, await store.CountForUserAsync(other.UserId, Token));
    }

    private static UserCalendarEventMapping Mapping(Guid userId, string stableIdentity) =>
        UserCalendarEventMapping.Create(
            userId,
            stableIdentity,
            Source,
            Guid.CreateVersion7(),
            "sirkadiyen@group.calendar.google.com",
            $"event-{stableIdentity}",
            $"sha256:{stableIdentity}",
            Now);

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
