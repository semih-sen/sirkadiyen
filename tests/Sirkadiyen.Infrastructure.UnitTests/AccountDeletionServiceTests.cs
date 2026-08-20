using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The orchestration in <see cref="AccountDeletionService"/> (ADR-118): the refusals, the
/// confirmation, the external cleanup ordering, and what the single audit record says for a
/// self-deletion versus an operator's.
/// </summary>
public sealed class AccountDeletionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private const string AccountEmail = "student@example.com";

    [Fact]
    public async Task RefusesToDeleteASuperAdmin()
    {
        AccountDeletionService service = Build(
            out FakeUserStore users,
            out FakeDeletionStore store,
            out FakeExternalCleanup cleanup,
            role: UserRole.SuperAdmin);

        AccountDeletionResult result = await service.DeleteAsync(SelfRequest(users.UserId), default);

        Assert.Equal(AccountDeletionOutcome.SuperAdminRefused, result.Outcome);
        Assert.False(store.DeleteCalled);
        Assert.False(cleanup.Called);
    }

    [Fact]
    public async Task RefusesAMismatchedConfirmationEmail()
    {
        AccountDeletionService service = Build(out FakeUserStore users, out FakeDeletionStore store, out _);

        AccountDeletionResult result = await service.DeleteAsync(
            SelfRequest(users.UserId) with { ConfirmEmail = "someone-else@example.com" },
            default);

        Assert.Equal(AccountDeletionOutcome.EmailMismatch, result.Outcome);
        Assert.False(store.DeleteCalled);
    }

    [Fact]
    public async Task ReportsAMissingUser()
    {
        AccountDeletionService service = Build(out FakeUserStore users, out FakeDeletionStore store, out _);
        users.Session = null;

        AccountDeletionResult result = await service.DeleteAsync(SelfRequest(Guid.NewGuid()), default);

        Assert.Equal(AccountDeletionOutcome.UserNotFound, result.Outcome);
        Assert.False(store.DeleteCalled);
    }

    [Fact]
    public async Task SelfDeletionCleansUpGoogleThenErasesAndRecordsSelfActor()
    {
        AccountDeletionService service = Build(
            out FakeUserStore users,
            out FakeDeletionStore store,
            out FakeExternalCleanup cleanup);
        cleanup.Result = new ExternalAccountCleanupResult
        {
            HadManagedCalendar = true,
            CalendarDeleted = true,
            TokenRevoked = true,
        };

        AccountDeletionResult result = await service.DeleteAsync(SelfRequest(users.UserId), default);

        Assert.Equal(AccountDeletionOutcome.Deleted, result.Outcome);
        Assert.True(cleanup.Called);
        Assert.True(store.DeleteCalled);
        // Google cleanup runs before the erasure — the credential lookup must precede the delete.
        Assert.True(cleanup.CalledBeforeDelete);
        Assert.True(result.GoogleCalendarDeleted);
        Assert.True(result.GoogleTokenRevoked);

        AuditEvent recorded = store.AppendedEvent!;
        Assert.Equal(AuditEventCategory.AccountDeleted, recorded.Category);
        Assert.Equal("User", recorded.SubjectType);
        Assert.Equal(users.UserId.ToString(), recorded.SubjectId);
        // Self-deletion: the actor is the account owner (the store then anonymizes it in the trail).
        Assert.Equal(users.UserId, recorded.ActorUserId);
        Assert.Equal(AccountEmail, recorded.ActorEmail);
        Assert.Null(recorded.Reason);
        Assert.Contains("\"requestedBy\":\"self\"", recorded.Metadata);
    }

    [Fact]
    public async Task OperatorDeletionKeepsTheOperatorAsActorAndRecordsTheReason()
    {
        Guid operatorId = Guid.NewGuid();
        AccountDeletionService service = Build(out FakeUserStore users, out FakeDeletionStore store, out _);

        AccountDeletionResult result = await service.DeleteAsync(
            new AccountDeletionRequest
            {
                UserId = users.UserId,
                RequestedByOperator = true,
                ActorUserId = operatorId,
                ActorEmail = "admin@example.com",
                ConfirmEmail = AccountEmail,
                Reason = "KVKK erasure request #42",
            },
            default);

        Assert.Equal(AccountDeletionOutcome.Deleted, result.Outcome);
        AuditEvent recorded = store.AppendedEvent!;
        Assert.Equal(operatorId, recorded.ActorUserId);
        Assert.Equal("admin@example.com", recorded.ActorEmail);
        Assert.Equal("KVKK erasure request #42", recorded.Reason);
        Assert.Contains("\"requestedBy\":\"operator\"", recorded.Metadata);
    }

    [Fact]
    public async Task ProceedsWhenTheAccountHasNoCalendarConnection()
    {
        AccountDeletionService service = Build(
            out FakeUserStore users,
            out FakeDeletionStore store,
            out FakeExternalCleanup cleanup);
        store.Cleanup = null; // no Google connection at all

        AccountDeletionResult result = await service.DeleteAsync(SelfRequest(users.UserId), default);

        Assert.Equal(AccountDeletionOutcome.Deleted, result.Outcome);
        Assert.False(cleanup.Called); // nothing external to clean up
        Assert.False(result.HadManagedCalendar);
        Assert.True(store.DeleteCalled);
    }

    private static AccountDeletionRequest SelfRequest(Guid userId) => new()
    {
        UserId = userId,
        RequestedByOperator = false,
        ActorUserId = userId,
        ActorEmail = AccountEmail,
        ConfirmEmail = AccountEmail,
    };

    private static AccountDeletionService Build(
        out FakeUserStore users,
        out FakeDeletionStore store,
        out FakeExternalCleanup cleanup,
        UserRole role = UserRole.User)
    {
        Guid userId = Guid.NewGuid();
        users = new FakeUserStore
        {
            UserId = userId,
            Session = new UserSession
            {
                UserId = userId,
                Email = AccountEmail,
                Role = role,
                LastSignedInAtUtc = Now,
            },
        };
        store = new FakeDeletionStore();
        cleanup = new FakeExternalCleanup(store);
        return new AccountDeletionService(
            users,
            store,
            cleanup,
            new PassthroughIpProtector(),
            new FixedTimeProvider(Now));
    }

    private sealed class FakeUserStore : IUserStore
    {
        public Guid UserId { get; set; }

        public UserSession? Session { get; set; }

        public Task<UserSession?> FindSessionAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Session);

        public Task<UserSession> SignInWithGoogleAsync(
            GoogleIdentity identity,
            UserRole bootstrapRole,
            DateTimeOffset signedInAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDeletionStore : IAccountDeletionStore
    {
        public bool DeleteCalled { get; private set; }

        public bool CleanupFetched { get; private set; }

        public AuditEvent? AppendedEvent { get; private set; }

        public AccountCalendarCleanup? Cleanup { get; set; } = new()
        {
            ProtectedRefreshToken = "cipher",
            ManagedCalendarId = "cal-1",
            Status = GoogleCalendarConnectionStatus.Authorized,
        };

        public Task<AccountCalendarCleanup?> GetCalendarCleanupAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            CleanupFetched = true;
            return Task.FromResult(Cleanup);
        }

        public Task<AccountDeletionStoreResult> DeleteAsync(
            Guid userId,
            AuditEvent accountDeletedEvent,
            CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            AppendedEvent = accountDeletedEvent;
            return Task.FromResult(new AccountDeletionStoreResult
            {
                Deleted = true,
                AnonymizedAuditEvents = 3,
            });
        }
    }

    private sealed class FakeExternalCleanup(FakeDeletionStore store) : IExternalAccountCleanup
    {
        public bool Called { get; private set; }

        public bool CalledBeforeDelete { get; private set; }

        public ExternalAccountCleanupResult Result { get; set; } = new()
        {
            HadManagedCalendar = true,
            CalendarDeleted = true,
            TokenRevoked = true,
        };

        public Task<ExternalAccountCleanupResult> CleanUpAsync(
            AccountCalendarCleanup credential,
            CancellationToken cancellationToken)
        {
            Called = true;
            CalledBeforeDelete = !store.DeleteCalled;
            return Task.FromResult(Result);
        }
    }

    private sealed class PassthroughIpProtector : IAuditIpProtector
    {
        public string Protect(string plaintextIp) => $"enc:{plaintextIp}";

        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
