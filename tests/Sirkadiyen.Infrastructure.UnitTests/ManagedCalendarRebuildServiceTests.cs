using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.GoogleCalendar;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the rebuild of a managed calendar the student deleted (ADR-116), and the domain reset
/// underneath it.
/// </summary>
/// <remarks>
/// The dead end it was built for is the concrete case: deleting the calendar marks the connection
/// unavailable, which drops the student out of every writer and makes onboarding report
/// <c>ActionRequired</c>, which routes them to the consent screen — and re-consenting does not
/// clear the flag, so it routes them there again. `ReauthorizingDoesNotClearTheUnavailableFlag`
/// pins that fact deliberately: the fix is this reset, not a change to re-authorization, because
/// a dead token and a deleted calendar are different problems with different repairs.
/// </remarks>
public sealed class ManagedCalendarRebuildServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1000);

    [Fact]
    public void ResettingDetachesTheCalendarSoInitialSyncCanFindOrCreateOneAgain()
    {
        // Detaching the id is the whole mechanism: initial sync only looks for a marker-matched
        // orphan, or creates a calendar, when no id is attached.
        GoogleCalendarConnection connection = UnavailableConnection();

        connection.ResetForCalendarRebuild(Now);

        Assert.Null(connection.ManagedCalendarId);
        Assert.Null(connection.ManagedCalendarUnavailableAtUtc);
        Assert.Equal(GoogleCalendarInitialSyncState.Pending, connection.InitialSyncState);
    }

    [Fact]
    public void ResettingLeavesTheSynchronizationForTheUserToStart()
    {
        // Populating a calendar is the user's decision to start (ADR-058), and a rebuild writes a
        // whole year of events. Pending is what the worker ignores until they press it.
        GoogleCalendarConnection connection = UnavailableConnection();

        connection.ResetForCalendarRebuild(Now);

        Assert.NotEqual(GoogleCalendarInitialSyncState.InProgress, connection.InitialSyncState);
        // Authorization is a separate axis and is deliberately untouched, so a connection that
        // also needs re-consent still passes through it first.
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
    }

    [Fact]
    public void ResettingClearsEveryPieceOfWorkScopedToTheCalendarThatIsGone()
    {
        // A reconciliation replay or a profile convergence queued against a deleted calendar would
        // ask the worker to converge something that does not exist. Initial sync resolves the whole
        // audience from the profile when it runs, which subsumes all of them.
        GoogleCalendarConnection connection = UnavailableConnection(withPendingWork: true);

        connection.ResetForCalendarRebuild(Now);

        Assert.Null(connection.ReconciliationRequiredSinceUtc);
        Assert.Null(connection.ReconciliationCursorDispatchedAtUtc);
        Assert.Null(connection.ReconciliationCursorDiffId);
        Assert.Null(connection.ProfileResyncRequiredSinceUtc);
        Assert.Null(connection.LastCalendarInventoryAtUtc);
    }

    [Fact]
    public void AHealthyConnectionCannotBeResetAtAll()
    {
        // A calendar the user merely hid from their list is still there, and inventory repairs it.
        // Discarding a working ledger for that would be the destructive answer to a non-problem.
        GoogleCalendarConnection connection = CompletedConnection();

        Assert.Throws<InvalidOperationException>(() => connection.ResetForCalendarRebuild(Now));
    }

    [Fact]
    public void ReauthorizingDoesNotClearTheUnavailableFlag()
    {
        // Pinned on purpose. This is exactly why the loop existed, and it stays true: a dead
        // credential and a deleted calendar are different problems, and re-consent repairs only
        // the first. The rebuild is what repairs the second.
        GoogleCalendarConnection connection = UnavailableConnection();

        connection.Reauthorize("protected-token", "scope", Now);

        Assert.NotNull(connection.ManagedCalendarUnavailableAtUtc);
    }

    [Fact]
    public async Task AssessingReportsWhyTheCalendarIsEligibleAndSinceWhenAsync()
    {
        DateTimeOffset unavailableSince = Now.AddDays(-3);
        ManagedCalendarRebuildService service = Service(View(unavailableSince));

        ManagedCalendarRebuildAssessment assessment =
            await service.AssessAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(ManagedCalendarRebuildOutcome.Reset, assessment.Outcome);
        Assert.Equal(unavailableSince, assessment.UnavailableSinceUtc);
    }

    [Fact]
    public async Task AHealthyConnectionIsReportedAsHavingNothingToRebuildAsync()
    {
        ManagedCalendarRebuildService service = Service(View(unavailableSince: null));

        ManagedCalendarRebuildAssessment assessment =
            await service.AssessAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(ManagedCalendarRebuildOutcome.NotEligible, assessment.Outcome);
    }

    [Fact]
    public async Task AUserWithNoConnectionIsReportedRatherThanTreatedAsEligibleAsync()
    {
        ManagedCalendarRebuildService service = Service(connection: null);

        ManagedCalendarRebuildAssessment assessment =
            await service.AssessAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(ManagedCalendarRebuildOutcome.NoConnection, assessment.Outcome);
    }

    [Fact]
    public async Task RequestingRebuildsAndReportsHowMuchLedgerWasDiscardedAsync()
    {
        RecordingConnectionStore store = new(View(Now.AddDays(-1)), discardedMappings: 412);
        Guid userId = Guid.CreateVersion7();

        ManagedCalendarRebuildResult result = await Service(store)
            .RequestAsync(userId, NoAudit, CancellationToken.None);

        Assert.Equal(ManagedCalendarRebuildOutcome.Reset, result.Outcome);
        // The count is what tells the student how large the rebuild is; hiding it would make an
        // entire year of re-written events look like nothing happened.
        Assert.Equal(412, result.DiscardedMappings);
        Assert.Equal(userId, store.RebuiltUserId);
    }

    [Fact]
    public async Task AnIneligibleConnectionIsRefusedBeforeAnythingIsDiscardedAsync()
    {
        RecordingConnectionStore store = new(View(unavailableSince: null), discardedMappings: 0);

        ManagedCalendarRebuildResult result = await Service(store)
            .RequestAsync(Guid.CreateVersion7(), NoAudit, CancellationToken.None);

        Assert.Equal(ManagedCalendarRebuildOutcome.NotEligible, result.Outcome);
        Assert.Null(store.RebuiltUserId);
    }

    [Fact]
    public async Task NothingIsDiscardedWhileFrozenAsync()
    {
        // A rebuild queues no calendar write of its own, but it does discard durable state, which
        // is what a freeze exists to stop until someone has looked (ADR-034/043).
        RecordingConnectionStore store = new(View(Now.AddDays(-1)), discardedMappings: 5);

        ManagedCalendarRebuildResult result = await Service(store, frozen: true)
            .RequestAsync(Guid.CreateVersion7(), NoAudit, CancellationToken.None);

        Assert.Equal(ManagedCalendarRebuildOutcome.Frozen, result.Outcome);
        Assert.Null(store.RebuiltUserId);
    }

    [Fact]
    public async Task AFailedAuditAbandonsTheRebuildBeforeAnyLedgerIsDiscardedAsync()
    {
        // The ordering is the guarantee: this discards a student's whole event ledger, so an
        // unrecordable request must leave it intact (AI_GUIDELINE §19).
        RecordingConnectionStore store = new(View(Now.AddDays(-1)), discardedMappings: 412);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store).RequestAsync(
                Guid.CreateVersion7(),
                (_, _) => throw new InvalidOperationException("the audit column rejected it"),
                CancellationToken.None));

        Assert.Null(store.RebuiltUserId);
    }

    private static Task NoAudit(
        ManagedCalendarRebuildAssessment assessment,
        CancellationToken cancellationToken) => Task.CompletedTask;

    private static GoogleCalendarConnection CompletedConnection()
    {
        GoogleCalendarConnection connection = GoogleCalendarConnection.Create(
            Guid.CreateVersion7(),
            "protected-token",
            "scope",
            DateTimeOffset.UnixEpoch);
        connection.RequestInitialSync(DateTimeOffset.UnixEpoch);
        connection.AttachManagedCalendar("calendar-id", DateTimeOffset.UnixEpoch);
        connection.CompleteInitialSync(DateTimeOffset.UnixEpoch);
        return connection;
    }

    private static GoogleCalendarConnection UnavailableConnection(bool withPendingWork = false)
    {
        GoogleCalendarConnection connection = CompletedConnection();

        if (withPendingWork)
        {
            connection.CompleteCalendarInventory(DateTimeOffset.UnixEpoch.AddHours(1));
            connection.TryRequestProfileResync(DateTimeOffset.UnixEpoch.AddHours(2));
            // A dead credential is what opens a reconciliation replay window.
            connection.MarkNeedsReauthorization(DateTimeOffset.UnixEpoch.AddHours(3));
            connection.Reauthorize("protected-token", "scope", DateTimeOffset.UnixEpoch.AddHours(4));
        }

        connection.MarkManagedCalendarUnavailable(DateTimeOffset.UnixEpoch.AddHours(5));
        return connection;
    }

    private static GoogleCalendarConnectionView View(DateTimeOffset? unavailableSince) => new()
    {
        UserId = Guid.CreateVersion7(),
        GrantedScopes = "scope",
        Status = GoogleCalendarConnectionStatus.Authorized,
        InitialSyncState = GoogleCalendarInitialSyncState.Completed,
        ManagedCalendarUnavailableAtUtc = unavailableSince,
        UpdatedAtUtc = Now,
    };

    private static ManagedCalendarRebuildService Service(
        GoogleCalendarConnectionView? connection,
        bool frozen = false) =>
        Service(new RecordingConnectionStore(connection, discardedMappings: 0), frozen);

    private static ManagedCalendarRebuildService Service(
        RecordingConnectionStore store,
        bool frozen = false) =>
        new(store, new StubFreezeStore(frozen), new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingConnectionStore(
        GoogleCalendarConnectionView? connection,
        int discardedMappings) : IUserCalendarConnectionStore
    {
        public Guid? RebuiltUserId { get; private set; }

        public Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(connection);

        public Task<ManagedCalendarRebuildResult> RebuildManagedCalendarAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            RebuiltUserId = userId;
            return Task.FromResult(new ManagedCalendarRebuildResult
            {
                Outcome = ManagedCalendarRebuildOutcome.Reset,
                DiscardedMappings = discardedMappings,
            });
        }

        public Task<GoogleCalendarConnectionView> UpsertAuthorizationAsync(
            Guid userId,
            string protectedRefreshToken,
            string grantedScopes,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestInitialSyncResult> RequestInitialSyncAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestReconciliationOutcome> RequestReconciliationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubFreezeStore(bool frozen) : IOperationalFreezeStore
    {
        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = frozen });

        public Task<bool> IsFrozenAsync(
            OperationalFreezeScope scope,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<OperationalFreezeSnapshot>> ListScopedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OperationalFreezeSnapshot>>([]);

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool isFrozen,
            string actorEmail,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationalFreezeChangeResult> SetScopedAsync(
            OperationalFreezeScope scope,
            bool isFrozen,
            string actorEmail,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
