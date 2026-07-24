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
        // A fresh authorization has not begun its initial sync (ADR-058).
        Assert.Equal(GoogleCalendarInitialSyncState.Pending, saved.InitialSyncState);

        GoogleCalendarConnectionView? read = await store.GetByUserIdAsync(user.UserId, Token);
        Assert.NotNull(read);
        Assert.Equal(Scope, read.GrantedScopes);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, read.Status);
        Assert.Equal(GoogleCalendarInitialSyncState.Pending, read.InitialSyncState);

        // The dedicated calendar is created later, by initial sync (ADR-024).
        Assert.Null(read.ManagedCalendarId);
    }

    [Fact]
    public async Task InitialSyncWalksFromRequestedThroughCalendarToCompleted()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-sync-walk");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await store.UpsertAuthorizationAsync(user.UserId, "protected", Scope, Now, Token);

        RequestInitialSyncResult requested = await store.RequestInitialSyncAsync(
            user.UserId,
            Now.AddMinutes(1),
            Token);
        Assert.Equal(RequestInitialSyncOutcome.Requested, requested.Outcome);

        // A second request is a harmless no-op that just reports the current state.
        Assert.Equal(
            RequestInitialSyncOutcome.AlreadyInProgress,
            (await store.RequestInitialSyncAsync(user.UserId, Now.AddMinutes(2), Token)).Outcome);

        // The user is now visible to the worker's pending queue, carrying the credential.
        IReadOnlyList<PendingCalendarSync> pending =
            await store.ListPendingInitialSyncAsync(10, Token);
        PendingCalendarSync mine = Assert.Single(
            pending,
            candidate => candidate.UserId == user.UserId);
        Assert.Equal("protected", mine.ProtectedRefreshToken);
        Assert.Null(mine.ManagedCalendarId);

        await store.AttachManagedCalendarAsync(
            user.UserId,
            "sirkadiyen@group.calendar.google.com",
            Now.AddMinutes(3),
            Token);
        await store.MarkInitialSyncCompletedAsync(user.UserId, Now.AddMinutes(4), Token);

        GoogleCalendarConnectionView? completed = await store.GetByUserIdAsync(user.UserId, Token);
        Assert.NotNull(completed);
        Assert.Equal(GoogleCalendarInitialSyncState.Completed, completed.InitialSyncState);
        Assert.Equal("sirkadiyen@group.calendar.google.com", completed.ManagedCalendarId);

        // A completed connection is no longer pending work.
        Assert.DoesNotContain(
            await store.ListPendingInitialSyncAsync(10, Token),
            candidate => candidate.UserId == user.UserId);
    }

    [Fact]
    public async Task RequestingInitialSyncForAnUnknownUserReportsNotFound()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);

        RequestInitialSyncResult result = await store.RequestInitialSyncAsync(
            Guid.CreateVersion7(),
            Now,
            Token);

        Assert.Equal(RequestInitialSyncOutcome.NotFound, result.Outcome);
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
    public async Task ReauthorizationExposesAndAdvancesADurableReconciliationCursor()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-reconciliation");
        DateTimeOffset failedAt = Now.AddDays(1);
        DateTimeOffset dispatchedAt = failedAt.AddMinutes(5);
        Guid diffId = Guid.CreateVersion7();

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await store.UpsertAuthorizationAsync(user.UserId, "first", Scope, Now, Token);
        await store.RequestInitialSyncAsync(user.UserId, Now.AddMinutes(1), Token);
        await store.AttachManagedCalendarAsync(
            user.UserId,
            "reconciliation@group.calendar.google.com",
            Now.AddMinutes(2),
            Token);
        await store.MarkInitialSyncCompletedAsync(user.UserId, Now.AddMinutes(3), Token);
        await store.MarkNeedsReauthorizationAsync(user.UserId, failedAt, Token);

        // A dead credential is not runnable work; the cursor waits durably for a fresh grant.
        Assert.DoesNotContain(
            await store.ListPendingReconciliationAsync(10, Token),
            candidate => candidate.UserId == user.UserId);

        await store.UpsertAuthorizationAsync(
            user.UserId,
            "fresh",
            Scope,
            failedAt.AddMinutes(1),
            Token);

        PendingCalendarReconciliation pending = Assert.Single(
            await store.ListPendingReconciliationAsync(10, Token),
            candidate => candidate.UserId == user.UserId);
        Assert.Equal(failedAt, pending.RequiredSinceUtc);
        Assert.Equal(failedAt, pending.CursorDispatchedAtUtc);
        Assert.Equal(Guid.Empty, pending.CursorDiffId);
        Assert.Equal("fresh", pending.ProtectedRefreshToken);

        await store.AdvanceReconciliationCursorAsync(
            user.UserId,
            failedAt,
            dispatchedAt,
            diffId,
            dispatchedAt.AddSeconds(1),
            Token);

        PendingCalendarReconciliation advanced = Assert.Single(
            await store.ListPendingReconciliationAsync(10, Token),
            candidate => candidate.UserId == user.UserId);
        Assert.Equal(dispatchedAt, advanced.CursorDispatchedAtUtc);
        Assert.Equal(diffId, advanced.CursorDiffId);

        await store.CompleteReconciliationAsync(
            user.UserId,
            failedAt,
            dispatchedAt.AddSeconds(2),
            Token);

        Assert.DoesNotContain(
            await store.ListPendingReconciliationAsync(10, Token),
            candidate => candidate.UserId == user.UserId);
        Assert.Null(
            (await store.GetByUserIdAsync(user.UserId, Token))!
                .ReconciliationRequiredSinceUtc);
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
