using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;
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
    public async Task InventoryHealthAndUnavailableCalendarStateRoundTrip()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-inventory-health");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await store.UpsertAuthorizationAsync(user.UserId, "protected", Scope, Now, Token);
        await store.RequestInitialSyncAsync(user.UserId, Now.AddMinutes(1), Token);
        await store.AttachManagedCalendarAsync(
            user.UserId,
            "inventory@group.calendar.google.com",
            Now.AddMinutes(2),
            Token);
        await store.MarkInitialSyncCompletedAsync(user.UserId, Now.AddMinutes(3), Token);

        await store.MarkCalendarInventoryCompletedAsync(user.UserId, Now.AddHours(1), Token);
        GoogleCalendarConnectionView healthy =
            (await store.GetByUserIdAsync(user.UserId, Token))!;
        Assert.Equal(Now.AddHours(1), healthy.LastCalendarInventoryAtUtc);
        Assert.Null(healthy.ManagedCalendarUnavailableAtUtc);

        await store.MarkManagedCalendarUnavailableAsync(user.UserId, Now.AddHours(2), Token);
        GoogleCalendarConnectionView unavailable =
            (await store.GetByUserIdAsync(user.UserId, Token))!;
        Assert.Equal(Now.AddHours(2), unavailable.ManagedCalendarUnavailableAtUtc);
        Assert.Equal(
            "inventory@group.calendar.google.com",
            unavailable.ManagedCalendarId);
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

    [Fact]
    public async Task RequestingReconciliationMakesAHealthyConnectionDue()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-reconcile-request");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await store.UpsertAuthorizationAsync(user.UserId, "protected", Scope, Now, Token);
        await store.RequestInitialSyncAsync(user.UserId, Now.AddMinutes(1), Token);
        await store.AttachManagedCalendarAsync(
            user.UserId,
            "reconcile@group.calendar.google.com",
            Now.AddMinutes(2),
            Token);
        await store.MarkInitialSyncCompletedAsync(user.UserId, Now.AddMinutes(3), Token);
        await store.MarkCalendarInventoryCompletedAsync(user.UserId, Now.AddHours(1), Token);

        RequestReconciliationOutcome outcome = await store.RequestReconciliationAsync(
            user.UserId,
            Now.AddHours(2),
            Token);

        Assert.Equal(RequestReconciliationOutcome.Requested, outcome);

        // The connection is now immediately due for the next inventory pass.
        GoogleCalendarConnectionView view = (await store.GetByUserIdAsync(user.UserId, Token))!;
        Assert.Null(view.LastCalendarInventoryAtUtc);
    }

    [Fact]
    public async Task ReconciliationIsNotEligibleBeforeInitialSyncCompletes()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("calendar-reconcile-pending");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await store.UpsertAuthorizationAsync(user.UserId, "protected", Scope, Now, Token);

        Assert.Equal(
            RequestReconciliationOutcome.NotEligible,
            await store.RequestReconciliationAsync(user.UserId, Now.AddMinutes(1), Token));
    }

    [Fact]
    public async Task ReconciliationForUnknownUserReportsNotFound()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Equal(
            RequestReconciliationOutcome.NotFound,
            await new GoogleCalendarConnectionStore(context).RequestReconciliationAsync(
                Guid.CreateVersion7(),
                Now,
                Token));
    }

    [Fact]
    public async Task ARevokedUserLeavesTheInitialSyncAndReplayQueues()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession syncing = await CreateUserAsync("gate-initial", activate: false);
        Guid syncingLicense = await ActivateAsync(syncing.UserId, "gate-initial");
        UserSession replaying = await CreateUserAsync("gate-replay", activate: false);
        Guid replayingLicense = await ActivateAsync(replaying.UserId, "gate-replay");

        DateTimeOffset failedAt = Now.AddHours(1);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);

        await store.UpsertAuthorizationAsync(syncing.UserId, "protected", Scope, Now, Token);
        await store.RequestInitialSyncAsync(syncing.UserId, Now.AddMinutes(1), Token);

        await store.UpsertAuthorizationAsync(replaying.UserId, "protected", Scope, Now, Token);
        await store.RequestInitialSyncAsync(replaying.UserId, Now.AddMinutes(1), Token);
        await store.AttachManagedCalendarAsync(
            replaying.UserId,
            "gate@group.calendar.google.com",
            Now.AddMinutes(2),
            Token);
        await store.MarkInitialSyncCompletedAsync(replaying.UserId, Now.AddMinutes(3), Token);
        await store.MarkNeedsReauthorizationAsync(replaying.UserId, failedAt, Token);
        await store.UpsertAuthorizationAsync(
            replaying.UserId,
            "fresh",
            Scope,
            failedAt.AddMinutes(1),
            Token);

        // Both are runnable work while their access is active.
        Assert.Contains(
            await store.ListPendingInitialSyncAsync(500, Token),
            candidate => candidate.UserId == syncing.UserId);
        Assert.Contains(
            await store.ListPendingReconciliationAsync(500, Token),
            candidate => candidate.UserId == replaying.UserId);

        await RevokeAsync(syncingLicense, "gate-initial");
        await RevokeAsync(replayingLicense, "gate-replay");

        // Revocation stops future synchronization on the next read (ADR-095). The in-progress
        // initial sync is not failed or rewound; it simply stops being listed, so restoring
        // access resumes it from what the ledger already holds.
        Assert.DoesNotContain(
            await store.ListPendingInitialSyncAsync(500, Token),
            candidate => candidate.UserId == syncing.UserId);
        Assert.DoesNotContain(
            await store.ListPendingReconciliationAsync(500, Token),
            candidate => candidate.UserId == replaying.UserId);

        GoogleCalendarConnectionView? preserved =
            await store.GetByUserIdAsync(syncing.UserId, Token);
        Assert.Equal(GoogleCalendarInitialSyncState.InProgress, preserved!.InitialSyncState);
    }

    [Fact]
    public async Task AProfileResyncRequestIsQueuedAndClearedByItsOwnWorker()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("resync-queue");
        DateTimeOffset requestedAt = Now.AddHours(1);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await CompleteConnectionAsync(store, user.UserId, "resync@group.calendar.google.com");

        // Not queued until a profile change asks for it.
        Assert.DoesNotContain(
            await store.ListPendingProfileResyncAsync(500, Token),
            candidate => candidate.UserId == user.UserId);

        await RequestResyncAsync(user.UserId, requestedAt);

        PendingProfileResync pending = Assert.Single(
            await store.ListPendingProfileResyncAsync(500, Token),
            candidate => candidate.UserId == user.UserId);
        Assert.Equal(requestedAt, pending.RequiredSinceUtc);
        Assert.Equal("resync@group.calendar.google.com", pending.ManagedCalendarId);
        Assert.Equal("protected", pending.ProtectedRefreshToken);

        // A fresh context, as the worker uses a scoped one per pass: completion must read the
        // committed request rather than an entity tracked from before it was written.
        await using SirkadiyenDbContext completion = fixture.CreateProductionLikeContext();
        Assert.Equal(
            CompleteProfileResyncOutcome.Completed,
            await new GoogleCalendarConnectionStore(completion).CompleteProfileResyncAsync(
                user.UserId,
                requestedAt,
                requestedAt.AddMinutes(5),
                Token));

        Assert.DoesNotContain(
            await store.ListPendingProfileResyncAsync(500, Token),
            candidate => candidate.UserId == user.UserId);
    }

    [Fact]
    public async Task AStaleWorkerCannotClearANewerProfileResyncRequest()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("resync-token");
        DateTimeOffset requestedAt = Now.AddHours(1);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await CompleteConnectionAsync(store, user.UserId, "token@group.calendar.google.com");
        await RequestResyncAsync(user.UserId, requestedAt);

        // The request timestamp is the workflow token. A pass that converged an older profile
        // must report Superseded rather than clearing a change made while it ran.
        await using SirkadiyenDbContext completion = fixture.CreateProductionLikeContext();
        Assert.Equal(
            CompleteProfileResyncOutcome.Superseded,
            await new GoogleCalendarConnectionStore(completion).CompleteProfileResyncAsync(
                user.UserId,
                requestedAt.AddMinutes(-30),
                requestedAt.AddMinutes(5),
                Token));

        Assert.Contains(
            await store.ListPendingProfileResyncAsync(500, Token),
            candidate => candidate.UserId == user.UserId);
    }

    [Fact]
    public async Task CompletingAProfileResyncForAnUnknownUserReportsNotFound()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Equal(
            CompleteProfileResyncOutcome.NotFound,
            await new GoogleCalendarConnectionStore(context).CompleteProfileResyncAsync(
                Guid.CreateVersion7(),
                Now,
                Now,
                Token));
    }

    [Fact]
    public async Task ARevokedUserLeavesTheProfileResyncQueue()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("resync-revoked", activate: false);
        Guid licenseId = await ActivateAsync(user.UserId, "resync-revoked");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await CompleteConnectionAsync(store, user.UserId, "revoked@group.calendar.google.com");
        await RequestResyncAsync(user.UserId, Now.AddHours(1));

        await RevokeAsync(licenseId, "resync-revoked");

        Assert.DoesNotContain(
            await store.ListPendingProfileResyncAsync(500, Token),
            candidate => candidate.UserId == user.UserId);
    }

    private async Task CompleteConnectionAsync(
        GoogleCalendarConnectionStore store,
        Guid userId,
        string calendarId)
    {
        await store.UpsertAuthorizationAsync(userId, "protected", Scope, Now, Token);
        await store.RequestInitialSyncAsync(userId, Now.AddMinutes(1), Token);
        await store.AttachManagedCalendarAsync(userId, calendarId, Now.AddMinutes(2), Token);
        await store.MarkInitialSyncCompletedAsync(userId, Now.AddMinutes(3), Token);
    }

    /// <summary>
    /// Records a resync request the way a profile write does, through the aggregate, on its own
    /// context so the queue read sees a committed row.
    /// </summary>
    private async Task RequestResyncAsync(Guid userId, DateTimeOffset atUtc)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnection connection = await context.GoogleCalendarConnections
            .SingleAsync(candidate => candidate.UserId == userId, Token);
        Assert.True(connection.TryRequestProfileResync(atUtc));
        await context.SaveChangesAsync(Token);
    }

    /// <summary>
    /// Signs a user in and, unless the case is about the licensing gate itself, activates them.
    /// Every worker queue requires an active license (ADR-095), so an unlicensed user is the
    /// exception rather than the default here.
    /// </summary>
    private async Task<UserSession> CreateUserAsync(string prefix, bool activate = true)
    {
        UserSession user = await SignInAsync(prefix);
        if (activate)
        {
            await ActivateAsync(user.UserId, prefix);
        }

        return user;
    }

    private async Task<Guid> ActivateAsync(Guid userId, string prefix)
    {
        UserSession admin = await SignInAsync($"{prefix}-admin");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        ManualLicenseActivationResult activation = await new LicenseStore(context)
            .ActivateManuallyAsync(
                userId,
                admin.UserId,
                admin.Email,
                "Seeded by the test.",
                Now,
                Token);

        return activation.LicenseId!.Value;
    }

    private async Task RevokeAsync(Guid licenseId, string prefix)
    {
        UserSession admin = await SignInAsync($"{prefix}-revoker");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await new LicenseStore(context).RevokeAsync(
            licenseId,
            admin.UserId,
            admin.Email,
            "Revoked by the test.",
            Now.AddMinutes(1),
            Token);
    }

    private async Task<UserSession> SignInAsync(string prefix)
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
