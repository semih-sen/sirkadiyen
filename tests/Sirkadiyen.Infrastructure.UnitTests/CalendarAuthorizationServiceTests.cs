using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class CalendarAuthorizationServiceTests
{
    private const string RequiredScope =
        "https://www.googleapis.com/auth/calendar.app.created";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 11, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task AGrantedAuthorizationIsStoredAgainstTheUser()
    {
        RecordingConnectionStore store = new();
        CalendarAuthorizationService service = Build(store);

        CalendarAuthorizationResult result = await service.AuthorizeAsync(
            UserId,
            "auth-code",
            CancellationToken.None);

        Assert.Equal(CalendarAuthorizationOutcome.Authorized, result.Outcome);
        Assert.NotNull(result.Connection);
        Assert.Equal(UserId, store.UserId);
        Assert.Equal(RequiredScope, store.GrantedScopes);
        Assert.Equal(Now, store.AtUtc);
    }

    [Fact]
    public async Task TheRefreshTokenIsEncryptedBeforeItReachesTheStore()
    {
        RecordingConnectionStore store = new();
        CalendarAuthorizationService service = Build(store);

        await service.AuthorizeAsync(UserId, "auth-code", CancellationToken.None);

        // The service must run the credential through the protector rather than hand the
        // raw Google token to persistence. That the real ciphertext reveals nothing is a
        // property of the protector, covered by its own tests.
        Assert.Equal(
            $"protected:{StubAuthorizationClient.RefreshToken}",
            store.ProtectedRefreshToken);
        Assert.NotEqual(StubAuthorizationClient.RefreshToken, store.ProtectedRefreshToken);
    }

    [Fact]
    public async Task TheRequiredScopeIsRecognizedAmongSeveralGrantedScopes()
    {
        RecordingConnectionStore store = new();
        CalendarAuthorizationService service = Build(
            store,
            client: new StubAuthorizationClient(
                grantedScopes: $"openid {RequiredScope} https://www.googleapis.com/auth/userinfo.email"));

        CalendarAuthorizationResult result = await service.AuthorizeAsync(
            UserId,
            "auth-code",
            CancellationToken.None);

        Assert.Equal(CalendarAuthorizationOutcome.Authorized, result.Outcome);
    }

    [Fact]
    public async Task AGrantWithoutTheCalendarScopeIsRefusedAndNotStored()
    {
        RecordingConnectionStore store = new();
        CalendarAuthorizationService service = Build(
            store,
            client: new StubAuthorizationClient(grantedScopes: "openid"));

        CalendarAuthorizationResult result = await service.AuthorizeAsync(
            UserId,
            "auth-code",
            CancellationToken.None);

        Assert.Equal(CalendarAuthorizationOutcome.InsufficientScope, result.Outcome);
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task ARejectedAuthorizationCodeIsReportedWithoutStoringAnything()
    {
        RecordingConnectionStore store = new();
        CalendarAuthorizationService service = Build(
            store,
            client: new StubAuthorizationClient(fail: true));

        CalendarAuthorizationResult result = await service.AuthorizeAsync(
            UserId,
            "auth-code",
            CancellationToken.None);

        Assert.Equal(CalendarAuthorizationOutcome.ExchangeFailed, result.Outcome);
        Assert.False(store.WasCalled);
    }

    [Theory]
    [InlineData(UserLicenseState.None, true)]
    [InlineData(UserLicenseState.Suspended, true)]
    [InlineData(UserLicenseState.Active, false)]
    public async Task AuthorizationRequiresAnActiveLicenseAndAProfile(
        UserLicenseState licenseState,
        bool hasProfile)
    {
        RecordingConnectionStore store = new();
        StubAuthorizationClient client = new();
        CalendarAuthorizationService service = Build(
            store,
            client,
            licenseState,
            hasProfile);

        CalendarAuthorizationResult result = await service.AuthorizeAsync(
            UserId,
            "auth-code",
            CancellationToken.None);

        Assert.Equal(CalendarAuthorizationOutcome.PrerequisitesNotMet, result.Outcome);
        Assert.False(store.WasCalled);

        // The code is never even sent to Google when the account may not connect.
        Assert.False(client.WasCalled);
    }

    private static CalendarAuthorizationService Build(
        RecordingConnectionStore store,
        StubAuthorizationClient? client = null,
        UserLicenseState licenseState = UserLicenseState.Active,
        bool hasProfile = true) =>
        new(
            client ?? new StubAuthorizationClient(),
            new StubTokenProtector(),
            store,
            new StubLicenseStore(licenseState),
            new StubProfileStore(hasProfile),
            new FixedTimeProvider(Now));

    private sealed class StubAuthorizationClient(
        string? grantedScopes = null,
        bool fail = false) : IGoogleCalendarAuthorizationClient
    {
        public const string RefreshToken = "google-refresh-token";

        public bool WasCalled { get; private set; }

        public string RequiredScope => CalendarAuthorizationServiceTests.RequiredScope;

        public string ClientId => "calendar-client-id";

        public Task<CalendarAuthorizationTokens> ExchangeAuthorizationCodeAsync(
            string authorizationCode,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return fail
                ? throw new GoogleCalendarAuthorizationException("rejected")
                : Task.FromResult(new CalendarAuthorizationTokens
                {
                    RefreshToken = RefreshToken,
                    GrantedScopes = grantedScopes ?? CalendarAuthorizationServiceTests.RequiredScope,
                });
        }
    }

    private sealed class StubTokenProtector : ICalendarTokenProtector
    {
        public string Protect(string plaintext) => $"protected:{plaintext}";

        public string Unprotect(string ciphertext) => ciphertext["protected:".Length..];
    }

    private sealed class RecordingConnectionStore : IGoogleCalendarConnectionStore
    {
        public bool WasCalled { get; private set; }

        public Guid UserId { get; private set; }

        public string? ProtectedRefreshToken { get; private set; }

        public string? GrantedScopes { get; private set; }

        public DateTimeOffset AtUtc { get; private set; }

        public Task<GoogleCalendarConnectionView> UpsertAuthorizationAsync(
            Guid userId,
            string protectedRefreshToken,
            string grantedScopes,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            UserId = userId;
            ProtectedRefreshToken = protectedRefreshToken;
            GrantedScopes = grantedScopes;
            AtUtc = atUtc;

            return Task.FromResult(new GoogleCalendarConnectionView
            {
                UserId = userId,
                GrantedScopes = grantedScopes,
                Status = GoogleCalendarConnectionStatus.Authorized,
                InitialSyncState = GoogleCalendarInitialSyncState.Pending,
                UpdatedAtUtc = atUtc,
            });
        }

        public Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestInitialSyncResult> RequestInitialSyncAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PendingCalendarSync>> ListPendingInitialSyncAsync(
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AttachManagedCalendarAsync(
            Guid userId,
            string managedCalendarId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkInitialSyncCompletedAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkNeedsReauthorizationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubProfileStore(bool hasProfile) : IStudentProfileStore
    {
        public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(hasProfile);

        public Task<StudentProfileView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StudentProfileView> UpsertAsync(
            Guid userId,
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            string studentNumber,
            string selectorSchemaVersion,
            IReadOnlyDictionary<string, string> selectors,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubLicenseStore(UserLicenseState state) : ILicenseStore
    {
        public Task<UserLicenseState> GetUserLicenseStateAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(state);

        public Task SaveCreatedAsync(
            License license,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LicenseRedemptionResult> RedeemAsync(
            byte[] codeHash,
            Guid userId,
            string userEmail,
            DateTimeOffset redeemedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LicenseRevocationResult> RevokeAsync(
            Guid licenseId,
            Guid actorUserId,
            string actorEmail,
            string reason,
            DateTimeOffset revokedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManualLicenseActivationResult> ActivateManuallyAsync(
            Guid userId,
            Guid actorUserId,
            string actorEmail,
            string reason,
            DateTimeOffset activatedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
