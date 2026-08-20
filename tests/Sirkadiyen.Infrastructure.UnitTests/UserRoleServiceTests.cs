using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>The guards in <see cref="UserRoleService"/> (ADR-119).</summary>
public sealed class UserRoleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task RefusesChangingYourOwnRole()
    {
        FakeUserStore users = new() { Session = Session("me@example.com", UserRole.SuperAdmin) };
        FakeRoleStore store = new();
        UserRoleService service = new(users, store, new FixedTimeProvider(Now));

        ChangeUserRoleServiceResult result = await service.ChangeRoleAsync(
            new ChangeUserRoleCommand { TargetUserId = Actor, ActorUserId = Actor, NewRole = UserRole.SuperAdmin },
            (_, _) => Task.CompletedTask,
            default);

        Assert.Equal(ChangeUserRoleServiceOutcome.CannotChangeOwnRole, result.Outcome);
        Assert.False(store.Called);
    }

    [Fact]
    public async Task RefusesDemotingTheBootstrapOperator()
    {
        FakeUserStore users = new()
        {
            Session = Session(GoogleSignInService.SuperAdminEmail, UserRole.SuperAdmin),
        };
        FakeRoleStore store = new();
        UserRoleService service = new(users, store, new FixedTimeProvider(Now));

        ChangeUserRoleServiceResult result = await service.ChangeRoleAsync(
            new ChangeUserRoleCommand { TargetUserId = users.Id, ActorUserId = Actor, NewRole = UserRole.User },
            (_, _) => Task.CompletedTask,
            default);

        Assert.Equal(ChangeUserRoleServiceOutcome.CannotDemoteBootstrapOperator, result.Outcome);
        Assert.False(store.Called);
    }

    [Fact]
    public async Task NoOpWhenTheRoleAlreadyMatches()
    {
        FakeUserStore users = new() { Session = Session("student@example.com", UserRole.User) };
        FakeRoleStore store = new();
        UserRoleService service = new(users, store, new FixedTimeProvider(Now));
        bool audited = false;

        ChangeUserRoleServiceResult result = await service.ChangeRoleAsync(
            new ChangeUserRoleCommand { TargetUserId = users.Id, ActorUserId = Actor, NewRole = UserRole.User },
            (_, _) => { audited = true; return Task.CompletedTask; },
            default);

        Assert.Equal(ChangeUserRoleServiceOutcome.Unchanged, result.Outcome);
        Assert.False(store.Called);
        Assert.False(audited); // an unchanged role is not a security event to record
    }

    [Fact]
    public async Task PromotesAStudentRecordingTheChangeFirst()
    {
        FakeUserStore users = new() { Session = Session("student@example.com", UserRole.User) };
        FakeRoleStore store = new();
        UserRoleService service = new(users, store, new FixedTimeProvider(Now));
        RoleChangeRecord? recorded = null;

        ChangeUserRoleServiceResult result = await service.ChangeRoleAsync(
            new ChangeUserRoleCommand { TargetUserId = users.Id, ActorUserId = Actor, NewRole = UserRole.SuperAdmin },
            (record, _) => { recorded = record; Assert.False(store.Called); return Task.CompletedTask; },
            default);

        Assert.Equal(ChangeUserRoleServiceOutcome.Changed, result.Outcome);
        Assert.True(store.Called);
        Assert.NotNull(recorded);
        Assert.Equal(UserRole.User, recorded!.PreviousRole);
        Assert.Equal(UserRole.SuperAdmin, recorded.NewRole);
    }

    [Fact]
    public async Task ReportsAMissingUser()
    {
        FakeUserStore users = new() { Session = null };
        FakeRoleStore store = new();
        UserRoleService service = new(users, store, new FixedTimeProvider(Now));

        ChangeUserRoleServiceResult result = await service.ChangeRoleAsync(
            new ChangeUserRoleCommand { TargetUserId = Guid.NewGuid(), ActorUserId = Actor, NewRole = UserRole.SuperAdmin },
            (_, _) => Task.CompletedTask,
            default);

        Assert.Equal(ChangeUserRoleServiceOutcome.UserNotFound, result.Outcome);
        Assert.False(store.Called);
    }

    private static UserSession Session(string email, UserRole role) => new()
    {
        UserId = Guid.NewGuid(),
        Email = email,
        Role = role,
        LastSignedInAtUtc = Now,
    };

    private sealed class FakeUserStore : FakeUserStoreBase
    {
        public Guid Id => Session?.UserId ?? Guid.Empty;

        public UserSession? Session { get; set; }

        public override Task<UserSession?> FindSessionAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Session);
    }

    private abstract class FakeUserStoreBase : IUserStore
    {
        public abstract Task<UserSession?> FindSessionAsync(Guid userId, CancellationToken cancellationToken);

        public Task<UserSession> SignInWithGoogleAsync(
            GoogleIdentity identity,
            UserRole bootstrapRole,
            DateTimeOffset signedInAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeRoleStore : IUserRoleStore
    {
        public bool Called { get; private set; }

        public Task<ChangeUserRoleResult> ChangeRoleAsync(
            Guid userId,
            UserRole newRole,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new ChangeUserRoleResult
            {
                Outcome = ChangeUserRoleOutcome.Changed,
                PreviousRole = UserRole.User,
                NewRole = newRole,
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
