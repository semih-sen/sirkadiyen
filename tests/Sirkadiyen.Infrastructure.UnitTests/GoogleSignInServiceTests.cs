using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class GoogleSignInServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExactVerifiedBootstrapEmailReceivesSuperAdminRole()
    {
        FakeUserStore store = new();
        GoogleSignInService service = Create(
            new GoogleIdentity
            {
                Subject = "admin-subject",
                Email = " HALIL.SEMIH.SEN@GMAIL.COM ",
                EmailVerified = true,
                DisplayName = "Admin",
            },
            store);

        UserSession session = await service.SignInAsync("credential", Token);

        Assert.Equal(UserRole.SuperAdmin, session.Role);
        Assert.Equal(UserRole.SuperAdmin, store.BootstrapRole);
        Assert.Equal(Now, store.SignedInAtUtc);
    }

    [Fact]
    public async Task AnyOtherVerifiedEmailReceivesUserRole()
    {
        FakeUserStore store = new();
        GoogleSignInService service = Create(
            new GoogleIdentity
            {
                Subject = "student-subject",
                Email = "student@example.com",
                EmailVerified = true,
            },
            store);

        UserSession session = await service.SignInAsync("credential", Token);

        Assert.Equal(UserRole.User, session.Role);
        Assert.Equal(UserRole.User, store.BootstrapRole);
    }

    [Fact]
    public async Task UnverifiedEmailNeverReachesTheUserStore()
    {
        FakeUserStore store = new();
        GoogleSignInService service = Create(
            new GoogleIdentity
            {
                Subject = "student-subject",
                Email = "student@example.com",
                EmailVerified = false,
            },
            store);

        await Assert.ThrowsAsync<InvalidGoogleCredentialException>(
            () => service.SignInAsync("credential", Token));
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task InvalidGoogleProfileNeverReachesTheUserStore()
    {
        FakeUserStore store = new();
        GoogleSignInService service = Create(
            new GoogleIdentity
            {
                Subject = new string('x', User.MaximumGoogleSubjectLength + 1),
                Email = "student@example.com",
                EmailVerified = true,
            },
            store);

        await Assert.ThrowsAsync<InvalidGoogleCredentialException>(
            () => service.SignInAsync("credential", Token));
        Assert.False(store.WasCalled);
    }

    private static GoogleSignInService Create(
        GoogleIdentity identity,
        FakeUserStore store) => new(
            new FakeGoogleIdentityVerifier(identity),
            store,
            new FixedTimeProvider(Now));

    private static readonly CancellationToken Token = CancellationToken.None;

    private sealed class FakeGoogleIdentityVerifier(GoogleIdentity identity)
        : IGoogleIdentityVerifier
    {
        public Task<GoogleIdentity> VerifyAsync(
            string credential,
            CancellationToken cancellationToken) => Task.FromResult(identity);
    }

    private sealed class FakeUserStore : IUserStore
    {
        public bool WasCalled { get; private set; }

        public UserRole? BootstrapRole { get; private set; }

        public DateTimeOffset? SignedInAtUtc { get; private set; }

        public Task<UserSession> SignInWithGoogleAsync(
            GoogleIdentity identity,
            UserRole bootstrapRole,
            DateTimeOffset signedInAtUtc,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            BootstrapRole = bootstrapRole;
            SignedInAtUtc = signedInAtUtc;
            return Task.FromResult(new UserSession
            {
                UserId = Guid.CreateVersion7(),
                Email = identity.Email.Trim(),
                DisplayName = identity.DisplayName,
                Role = bootstrapRole,
                LastSignedInAtUtc = signedInAtUtc,
            });
        }

        public Task<UserSession?> FindSessionAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserSession?>(null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
