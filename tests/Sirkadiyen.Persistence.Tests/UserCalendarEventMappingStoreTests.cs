using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
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
    public async Task InventoryListsOnlyTheRequestedUsersCompleteLedger()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession owner = await CreateUserAsync("mapping-inventory-owner");
        UserSession other = await CreateUserAsync("mapping-inventory-other");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);
        await store.AddAsync(Mapping(owner.UserId, "z-last"), Token);
        await store.AddAsync(Mapping(owner.UserId, "a-first"), Token);
        await store.AddAsync(Mapping(other.UserId, "not-mine"), Token);

        IReadOnlyList<CalendarEventMappingView> inventory =
            await store.ListForUserAsync(owner.UserId, Token);

        Assert.Equal(["a-first", "z-last"], inventory.Select(item => item.StableIdentity));
        Assert.All(inventory, item => Assert.Equal(owner.UserId, item.UserId));
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

    [Fact]
    public async Task TheReverseLookupNamesEveryHolderOfALessonScopedToItsSource()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession first = await CreateUserAsync("reverse-first");
        UserSession second = await CreateUserAsync("reverse-second");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);
        await store.AddAsync(Mapping(first.UserId, "shared-lesson"), Token);
        await store.AddAsync(Mapping(second.UserId, "shared-lesson"), Token);
        // A same-named lesson from a different source must not be swept into the fan-out.
        await store.AddAsync(
            Mapping(first.UserId, "shared-lesson", SourceId.Parse("G2-EN-ANNUAL")),
            Token);

        IReadOnlyList<CalendarEventMappingView> holders =
            await store.ListForStableIdentityAsync(Source, "shared-lesson", Token);

        Assert.Equal(2, holders.Count);
        Assert.Contains(holders, holder => holder.UserId == first.UserId);
        Assert.Contains(holders, holder => holder.UserId == second.UserId);
        Assert.All(holders, holder => Assert.Equal("sha256:shared-lesson", holder.ContentHash));
    }

    [Fact]
    public async Task UpdatingContentReplacesTheStoredHashAndRecord()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("update-content");
        Guid newRecordId = Guid.CreateVersion7();

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);
        await store.AddAsync(Mapping(user.UserId, "patched"), Token);

        await store.UpdateContentAsync(
            user.UserId,
            "patched",
            newRecordId,
            "sha256:patched-v2",
            Now.AddMinutes(5),
            Token);

        CalendarEventMappingView holder = Assert.Single(
            await store.ListForStableIdentityAsync(Source, "patched", Token));
        Assert.Equal("sha256:patched-v2", holder.ContentHash);
        Assert.Equal(newRecordId, holder.CanonicalRecordId);
    }

    [Fact]
    public async Task ReidentifyingAMappingPreservesTheGoogleEventAndIsIdempotent()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("reidentify-mapping");
        Guid newRecordId = Guid.CreateVersion7();

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);
        await store.AddAsync(Mapping(user.UserId, "lesson-at-0900"), Token);

        CalendarEventMappingReidentifyOutcome moved = await store.ReidentifyAsync(
            user.UserId,
            Source,
            "lesson-at-0900",
            "lesson-at-1000",
            newRecordId,
            "sha256:moved",
            Now.AddMinutes(5),
            Token);

        Assert.Equal(CalendarEventMappingReidentifyOutcome.Reidentified, moved);
        Assert.Empty(await store.ListForStableIdentityAsync(Source, "lesson-at-0900", Token));
        CalendarEventMappingView current = Assert.Single(
            await store.ListForStableIdentityAsync(Source, "lesson-at-1000", Token));
        Assert.Equal("event-lesson-at-0900", current.GoogleEventId);
        Assert.Equal("sha256:moved", current.ContentHash);
        Assert.Equal(newRecordId, current.CanonicalRecordId);

        CalendarEventMappingReidentifyOutcome replay = await store.ReidentifyAsync(
            user.UserId,
            Source,
            "lesson-at-0900",
            "lesson-at-1000",
            newRecordId,
            "sha256:moved",
            Now.AddMinutes(6),
            Token);
        Assert.Equal(CalendarEventMappingReidentifyOutcome.AlreadyReidentified, replay);
        Assert.Equal(1, await store.CountForUserAsync(user.UserId, Token));
    }

    [Fact]
    public async Task RemovingAMappingDeletesItAndAnAbsentOneIsReported()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("remove-mapping");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);
        await store.AddAsync(Mapping(user.UserId, "to-remove"), Token);

        Assert.Equal(
            CalendarEventMappingRemoveOutcome.Removed,
            await store.RemoveAsync(user.UserId, "to-remove", Token));
        Assert.Equal(0, await store.CountForUserAsync(user.UserId, Token));

        // A resumed dispatch that deletes a mapping already gone must converge, not fail.
        Assert.Equal(
            CalendarEventMappingRemoveOutcome.NotFound,
            await store.RemoveAsync(user.UserId, "to-remove", Token));
    }

    [Fact]
    public async Task ProgressCountsMappedAndPatchedEventsForTheUser()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("mapping-progress");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);
        await store.AddAsync(Mapping(user.UserId, "created-1"), Token);
        await store.AddAsync(Mapping(user.UserId, "created-2"), Token);
        await store.AddAsync(Mapping(user.UserId, "patched-1"), Token);

        DateTimeOffset later = Now.AddDays(1);
        await store.UpdateContentAsync(
            user.UserId,
            "patched-1",
            Guid.CreateVersion7(),
            "sha256:patched-1-v2",
            later,
            Token);

        CalendarSyncProgressView progress =
            await store.GetProgressForUserAsync(user.UserId, Token);

        Assert.Equal(3, progress.MappedEventCount);
        Assert.Equal(1, progress.UpdatedEventCount);
        Assert.Equal(Now, progress.FirstWrittenAtUtc);
        Assert.Equal(later, progress.LastWrittenAtUtc);
    }

    [Fact]
    public async Task ProgressIsAllZeroWhenTheUserHasNoMappings()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("mapping-progress-empty");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserCalendarEventMappingStore store = new(context);

        CalendarSyncProgressView progress =
            await store.GetProgressForUserAsync(user.UserId, Token);

        Assert.Equal(0, progress.MappedEventCount);
        Assert.Equal(0, progress.UpdatedEventCount);
        Assert.Null(progress.FirstWrittenAtUtc);
        Assert.Null(progress.LastWrittenAtUtc);
    }

    private static UserCalendarEventMapping Mapping(
        Guid userId,
        string stableIdentity,
        SourceId? sourceId = null) =>
        UserCalendarEventMapping.Create(
            userId,
            stableIdentity,
            sourceId ?? Source,
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
