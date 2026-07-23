using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class GoogleCalendarConnectionStoreTests(PostgresFixture fixture)
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.app.created";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnAuthorizationIsInsertedAndReadBack()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-insert");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);

        GoogleCalendarConnectionView saved = await store.UpsertAuthorizationAsync(
            user.UserId,
            "protected-token-one",
            Scope,
            Now,
            Token);

        Assert.Equal(user.UserId, saved.UserId);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, saved.Status);
        Assert.True(await store.IsAuthorizedForUserAsync(user.UserId, Token));

        GoogleCalendarConnectionView? read = await store.GetByUserIdAsync(user.UserId, Token);
        Assert.NotNull(read);
        Assert.Equal(Scope, read.GrantedScopes);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, read.Status);

        // The dedicated calendar is created later, by initial sync (ADR-024).
        Assert.Null(read.ManagedCalendarId);
    }

    [Fact]
    public async Task TheStoredCredentialIsPersistedExactlyAsGiven()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-credential");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await new GoogleCalendarConnectionStore(context).UpsertAuthorizationAsync(
            user.UserId,
            "protected-ciphertext",
            Scope,
            Now,
            Token);

        // The read projection deliberately omits the credential, so verify the column
        // itself round-trips what the application layer encrypted.
        await using SirkadiyenDbContext verification = fixture.CreateContext();
        GoogleCalendarConnection stored = await verification.GoogleCalendarConnections
            .AsNoTracking()
            .SingleAsync(connection => connection.UserId == user.UserId, Token);

        Assert.Equal("protected-ciphertext", stored.ProtectedRefreshToken);
    }

    [Fact]
    public async Task ReAuthorizingReplacesTheSameRowRatherThanAddingASecond()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-reauth");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);

        await store.UpsertAuthorizationAsync(user.UserId, "first", Scope, Now, Token);
        GoogleCalendarConnectionView updated = await store.UpsertAuthorizationAsync(
            user.UserId,
            "second",
            $"{Scope} openid",
            Now.AddDays(30),
            Token);

        Assert.Equal($"{Scope} openid", updated.GrantedScopes);
        Assert.Equal(Now.AddDays(30), updated.UpdatedAtUtc);

        await using SirkadiyenDbContext verification = fixture.CreateContext();
        Assert.Equal(
            1,
            await verification.GoogleCalendarConnections.CountAsync(
                connection => connection.UserId == user.UserId,
                Token));
        Assert.Equal(
            "second",
            (await verification.GoogleCalendarConnections
                .AsNoTracking()
                .SingleAsync(connection => connection.UserId == user.UserId, Token))
                .ProtectedRefreshToken);
    }

    [Fact]
    public async Task ConcurrentFirstTimeAuthorizationsConvergeOnOneRow()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-race");

        await using SirkadiyenDbContext firstContext = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext secondContext = fixture.CreateProductionLikeContext();

        await Task.WhenAll(
            new GoogleCalendarConnectionStore(firstContext).UpsertAuthorizationAsync(
                user.UserId,
                "token-a",
                Scope,
                Now,
                Token),
            new GoogleCalendarConnectionStore(secondContext).UpsertAuthorizationAsync(
                user.UserId,
                "token-b",
                Scope,
                Now.AddSeconds(1),
                Token));

        await using SirkadiyenDbContext verification = fixture.CreateContext();
        Assert.Equal(
            1,
            await verification.GoogleCalendarConnections.CountAsync(
                connection => connection.UserId == user.UserId,
                Token));
    }

    [Fact]
    public async Task NoConnectionReadsAsAbsent()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-absent");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        GoogleCalendarConnectionStore store = new(context);

        Assert.False(await store.IsAuthorizedForUserAsync(user.UserId, Token));
        Assert.Null(await store.GetByUserIdAsync(user.UserId, Token));
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
