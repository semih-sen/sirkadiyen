using Sirkadiyen.Domain.GoogleCalendar;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class GoogleCalendarConnectionTests
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.app.created";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void CreateStartsAuthorizedAndTrimsTheStoredValues()
    {
        GoogleCalendarConnection connection = GoogleCalendarConnection.Create(
            UserId,
            "  protected-token  ",
            $" {Scope} ",
            Now);

        Assert.NotEqual(Guid.Empty, connection.Id);
        Assert.Equal(UserId, connection.UserId);
        Assert.Equal("protected-token", connection.ProtectedRefreshToken);
        Assert.Equal(Scope, connection.GrantedScopes);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
        Assert.Equal(Now, connection.CreatedAtUtc);
        Assert.Equal(Now, connection.UpdatedAtUtc);
    }

    [Fact]
    public void ANewConnectionHasNoManagedCalendarYet()
    {
        // The dedicated calendar is created by initial sync, not by authorization
        // (ADR-024), so a freshly authorized connection must not claim one.
        GoogleCalendarConnection connection = Create();

        Assert.Null(connection.ManagedCalendarId);
    }

    [Fact]
    public void ReauthorizingReplacesTheCredentialAndKeepsIdentity()
    {
        GoogleCalendarConnection connection = Create();
        Guid id = connection.Id;

        connection.Reauthorize("second-token", Scope, Now.AddDays(30));

        Assert.Equal(id, connection.Id);
        Assert.Equal(UserId, connection.UserId);
        Assert.Equal("second-token", connection.ProtectedRefreshToken);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
        Assert.Equal(Now, connection.CreatedAtUtc);
        Assert.Equal(Now.AddDays(30), connection.UpdatedAtUtc);
    }

    [Fact]
    public void AConnectionMustHaveAnOwner() =>
        Assert.Throws<ArgumentException>(() => Create(userId: Guid.Empty));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCredentialOrScopeIsRejected(string blank)
    {
        Assert.Throws<ArgumentException>(() => Create(protectedRefreshToken: blank));
        Assert.Throws<ArgumentException>(() => Create(grantedScopes: blank));
    }

    [Fact]
    public void ACredentialLongerThanTheBoundIsRejected()
    {
        string tooLong = new(
            'x',
            GoogleCalendarConnection.MaximumProtectedRefreshTokenLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(protectedRefreshToken: tooLong));
    }

    [Fact]
    public void CreateStartsWithInitialSyncPending() =>
        Assert.Equal(GoogleCalendarInitialSyncState.Pending, Create().InitialSyncState);

    [Fact]
    public void RequestingInitialSyncMovesAPendingConnectionToInProgress()
    {
        GoogleCalendarConnection connection = Create();

        connection.RequestInitialSync(Now.AddMinutes(1));

        Assert.Equal(GoogleCalendarInitialSyncState.InProgress, connection.InitialSyncState);
        Assert.Equal(Now.AddMinutes(1), connection.UpdatedAtUtc);
    }

    [Fact]
    public void RequestingInitialSyncTwiceIsRejected()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now);

        Assert.Throws<InvalidOperationException>(() => connection.RequestInitialSync(Now));
    }

    [Fact]
    public void AManagedCalendarIsAttachedExactlyOnce()
    {
        GoogleCalendarConnection connection = Create();

        connection.AttachManagedCalendar("calendar-id", Now.AddMinutes(1));
        Assert.Equal("calendar-id", connection.ManagedCalendarId);

        // The calendar the user's events already live in must not be silently replaced.
        Assert.Throws<InvalidOperationException>(
            () => connection.AttachManagedCalendar("another-id", Now.AddMinutes(2)));
    }

    [Fact]
    public void InitialSyncCannotCompleteBeforeItStarts() =>
        Assert.Throws<InvalidOperationException>(() => Create().CompleteInitialSync(Now));

    [Fact]
    public void InitialSyncCannotCompleteWithoutACalendar()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now);

        Assert.Throws<InvalidOperationException>(() => connection.CompleteInitialSync(Now));
    }

    [Fact]
    public void InitialSyncCompletesOnceStartedAndGivenACalendar()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now.AddMinutes(1));
        connection.AttachManagedCalendar("calendar-id", Now.AddMinutes(2));

        connection.CompleteInitialSync(Now.AddMinutes(3));

        Assert.Equal(GoogleCalendarInitialSyncState.Completed, connection.InitialSyncState);
        Assert.Equal(Now.AddMinutes(3), connection.UpdatedAtUtc);
    }

    [Fact]
    public void ReauthorizingPreservesTheCalendarAndInitialSyncProgress()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now.AddMinutes(1));
        connection.AttachManagedCalendar("calendar-id", Now.AddMinutes(2));
        connection.CompleteInitialSync(Now.AddMinutes(3));

        connection.Reauthorize("fresh-token", Scope, Now.AddDays(30));

        // A user who re-grants access keeps their calendar and does not re-run initial sync.
        Assert.Equal("calendar-id", connection.ManagedCalendarId);
        Assert.Equal(GoogleCalendarInitialSyncState.Completed, connection.InitialSyncState);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
    }

    [Fact]
    public void MarkingNeedsReauthorizationStopsSyncButKeepsTheCalendarAndProgress()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now.AddMinutes(1));
        connection.AttachManagedCalendar("calendar-id", Now.AddMinutes(2));
        connection.CompleteInitialSync(Now.AddMinutes(3));

        connection.MarkNeedsReauthorization(Now.AddDays(1));

        // A dead token means we cannot write, not that what was written is wrong (ADR-059).
        Assert.Equal(GoogleCalendarConnectionStatus.NeedsReauthorization, connection.Status);
        Assert.Equal("calendar-id", connection.ManagedCalendarId);
        Assert.Equal(GoogleCalendarInitialSyncState.Completed, connection.InitialSyncState);
        Assert.Equal(Now.AddDays(1), connection.UpdatedAtUtc);
        Assert.Equal(Now.AddDays(1), connection.ReconciliationRequiredSinceUtc);
        Assert.Equal(Now.AddDays(1), connection.ReconciliationCursorDispatchedAtUtc);
        Assert.Equal(Guid.Empty, connection.ReconciliationCursorDiffId);
    }

    [Fact]
    public void ACredentialFailureBeforeInitialSyncCompletesDoesNotQueueReconciliation()
    {
        GoogleCalendarConnection connection = Create();

        connection.MarkNeedsReauthorization(Now.AddDays(1));

        // Initial sync remains the authoritative way to reach current state; diff replay
        // is only for users whose one-time population had already completed.
        Assert.Null(connection.ReconciliationRequiredSinceUtc);
        Assert.Null(connection.ReconciliationCursorDispatchedAtUtc);
        Assert.Null(connection.ReconciliationCursorDiffId);
    }

    [Fact]
    public void MarkingNeedsReauthorizationTwiceIsANoOp()
    {
        GoogleCalendarConnection connection = Create();
        connection.MarkNeedsReauthorization(Now.AddDays(1));

        connection.MarkNeedsReauthorization(Now.AddDays(2));

        // The second call must not advance the timestamp: nothing changed.
        Assert.Equal(Now.AddDays(1), connection.UpdatedAtUtc);
    }

    [Fact]
    public void ReauthorizingRestoresAConnectionThatNeededReauthorization()
    {
        GoogleCalendarConnection connection = Create();
        connection.MarkNeedsReauthorization(Now.AddDays(1));

        connection.Reauthorize("fresh-token", Scope, Now.AddDays(2));

        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
    }

    [Fact]
    public void ReauthorizingACompletedConnectionPreservesItsReconciliationCursor()
    {
        GoogleCalendarConnection connection = CompletedConnection();
        DateTimeOffset failedAt = Now.AddDays(1);
        connection.MarkNeedsReauthorization(failedAt);

        connection.Reauthorize("fresh-token", Scope, Now.AddDays(2));

        Assert.Equal(failedAt, connection.ReconciliationRequiredSinceUtc);
        Assert.Equal(failedAt, connection.ReconciliationCursorDispatchedAtUtc);
        Assert.Equal(Guid.Empty, connection.ReconciliationCursorDiffId);
    }

    [Fact]
    public void ReconciliationCursorAdvancesMonotonicallyAndThenClears()
    {
        GoogleCalendarConnection connection = CompletedConnection();
        DateTimeOffset failedAt = Now.AddDays(1);
        DateTimeOffset dispatchedAt = failedAt.AddMinutes(5);
        Guid diffId = Guid.CreateVersion7();
        connection.MarkNeedsReauthorization(failedAt);
        connection.Reauthorize("fresh-token", Scope, failedAt.AddMinutes(1));

        connection.AdvanceReconciliationCursor(
            failedAt,
            dispatchedAt,
            diffId,
            dispatchedAt.AddSeconds(1));

        Assert.Equal(dispatchedAt, connection.ReconciliationCursorDispatchedAtUtc);
        Assert.Equal(diffId, connection.ReconciliationCursorDiffId);
        Assert.Throws<InvalidOperationException>(() => connection.AdvanceReconciliationCursor(
            failedAt,
            dispatchedAt,
            diffId,
            dispatchedAt.AddSeconds(2)));

        connection.CompleteReconciliation(failedAt, dispatchedAt.AddSeconds(3));

        Assert.Null(connection.ReconciliationRequiredSinceUtc);
        Assert.Null(connection.ReconciliationCursorDispatchedAtUtc);
        Assert.Null(connection.ReconciliationCursorDiffId);
    }

    [Fact]
    public void AHealthyCaughtUpConnectionRecordsInventoryCompletionMonotonically()
    {
        GoogleCalendarConnection connection = CompletedConnection();

        connection.CompleteCalendarInventory(Now.AddHours(1));
        connection.CompleteCalendarInventory(Now.AddHours(2));

        Assert.Equal(Now.AddHours(2), connection.LastCalendarInventoryAtUtc);
        Assert.Throws<InvalidOperationException>(
            () => connection.CompleteCalendarInventory(Now.AddMinutes(30)));
    }

    [Fact]
    public void AnUnavailableCalendarStopsInventoryAndPreservesItsIdentity()
    {
        GoogleCalendarConnection connection = CompletedConnection();

        connection.MarkManagedCalendarUnavailable(Now.AddHours(1));

        Assert.Equal(Now.AddHours(1), connection.ManagedCalendarUnavailableAtUtc);
        Assert.Equal("calendar-id", connection.ManagedCalendarId);
        Assert.Throws<InvalidOperationException>(
            () => connection.CompleteCalendarInventory(Now.AddHours(2)));
    }

    [Fact]
    public void InventoryCannotCompleteWhileSemanticReplayIsPending()
    {
        GoogleCalendarConnection connection = CompletedConnection();
        connection.MarkNeedsReauthorization(Now.AddHours(1));
        connection.Reauthorize("fresh", Scope, Now.AddHours(2));

        Assert.Throws<InvalidOperationException>(
            () => connection.CompleteCalendarInventory(Now.AddHours(3)));
    }

    [Fact]
    public void PresentationChangeMakesACompletedCalendarImmediatelyDueForInventory()
    {
        GoogleCalendarConnection connection = CompletedConnection();
        connection.CompleteCalendarInventory(Now.AddHours(1));

        connection.RequestCalendarPresentationRefresh(Now.AddHours(2));

        Assert.Null(connection.LastCalendarInventoryAtUtc);
        Assert.Equal(Now.AddHours(2), connection.UpdatedAtUtc);
        Assert.Equal(GoogleCalendarInitialSyncState.Completed, connection.InitialSyncState);
    }

    [Fact]
    public void AProfileChangeOnACompletedConnectionRecordsAResyncRequest()
    {
        GoogleCalendarConnection connection = CompletedConnection();

        Assert.True(connection.TryRequestProfileResync(Now.AddHours(1)));
        Assert.Equal(Now.AddHours(1), connection.ProfileResyncRequiredSinceUtc);
        Assert.Equal(Now.AddHours(1), connection.UpdatedAtUtc);
    }

    [Fact]
    public void ASecondProfileChangeKeepsTheOldestUnconvergedRequest()
    {
        // The queue is ordered by the request time, so pushing it forward on every change would
        // let a student who keeps editing starve the others.
        GoogleCalendarConnection connection = CompletedConnection();
        connection.TryRequestProfileResync(Now.AddHours(1));

        connection.TryRequestProfileResync(Now.AddHours(2));

        Assert.Equal(Now.AddHours(1), connection.ProfileResyncRequiredSinceUtc);
    }

    [Fact]
    public void AConnectionWhoseInitialSyncHasNotFinishedNeedsNoResyncRequest()
    {
        // Initial sync resolves the audience from the profile as it stands when it runs.
        GoogleCalendarConnection connection = Create();

        Assert.False(connection.TryRequestProfileResync(Now.AddHours(1)));
        Assert.Null(connection.ProfileResyncRequiredSinceUtc);
    }

    [Fact]
    public void CompletingAResyncClearsItsRequest()
    {
        GoogleCalendarConnection connection = CompletedConnection();
        connection.TryRequestProfileResync(Now.AddHours(1));

        connection.CompleteProfileResync(Now.AddHours(1), Now.AddHours(2));

        Assert.Null(connection.ProfileResyncRequiredSinceUtc);
        Assert.Equal(Now.AddHours(2), connection.UpdatedAtUtc);
    }

    [Fact]
    public void AStaleWorkerCannotClearANewerResyncRequest()
    {
        // The request timestamp is an optimistic workflow token: a pass that converged the older
        // profile must not clear a change made while it ran.
        GoogleCalendarConnection connection = CompletedConnection();
        connection.TryRequestProfileResync(Now.AddHours(1));

        Assert.Throws<InvalidOperationException>(
            () => connection.CompleteProfileResync(Now.AddMinutes(30), Now.AddHours(2)));
        Assert.Equal(Now.AddHours(1), connection.ProfileResyncRequiredSinceUtc);
    }

    [Fact]
    public void AResyncRequestSurvivesReauthorization()
    {
        // A dead credential is not a failure of the request: it waits for the new grant.
        GoogleCalendarConnection connection = CompletedConnection();
        connection.TryRequestProfileResync(Now.AddHours(1));
        connection.MarkNeedsReauthorization(Now.AddHours(2));

        connection.Reauthorize("protected-token-2", Scope, Now.AddHours(3));

        Assert.Equal(Now.AddHours(1), connection.ProfileResyncRequiredSinceUtc);
    }

    private static GoogleCalendarConnection Create(
        Guid? userId = null,
        string protectedRefreshToken = "protected-token",
        string grantedScopes = Scope) =>
        GoogleCalendarConnection.Create(
            userId ?? UserId,
            protectedRefreshToken,
            grantedScopes,
            Now);

    private static GoogleCalendarConnection CompletedConnection()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now.AddMinutes(1));
        connection.AttachManagedCalendar("calendar-id", Now.AddMinutes(2));
        connection.CompleteInitialSync(Now.AddMinutes(3));
        return connection;
    }
}
