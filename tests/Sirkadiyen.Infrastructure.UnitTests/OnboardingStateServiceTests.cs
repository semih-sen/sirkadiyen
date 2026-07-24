using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class OnboardingStateServiceTests
{
    public enum ConnectionFixture
    {
        None,
        NeedsReauthorization,
        AuthorizedPending,
        AuthorizedInProgress,
        AuthorizedCompleted,
    }

    [Theory]
    [InlineData(
        UserLicenseState.None,
        false,
        ConnectionFixture.None,
        OnboardingState.LicenseRequired,
        OnboardingNextAction.RedeemLicense,
        false)]
    [InlineData(
        UserLicenseState.Suspended,
        false,
        ConnectionFixture.None,
        OnboardingState.Suspended,
        OnboardingNextAction.ContactSupport,
        false)]
    [InlineData(
        UserLicenseState.Active,
        false,
        ConnectionFixture.None,
        OnboardingState.ProfileRequired,
        OnboardingNextAction.CompleteAcademicProfile,
        true)]
    [InlineData(
        UserLicenseState.Active,
        true,
        ConnectionFixture.None,
        OnboardingState.CalendarAuthorizationRequired,
        OnboardingNextAction.AuthorizeCalendar,
        true)]
    [InlineData(
        UserLicenseState.Active,
        true,
        ConnectionFixture.NeedsReauthorization,
        OnboardingState.CalendarAuthorizationRequired,
        OnboardingNextAction.AuthorizeCalendar,
        true)]
    [InlineData(
        UserLicenseState.Active,
        true,
        ConnectionFixture.AuthorizedPending,
        OnboardingState.ReadyForInitialSync,
        OnboardingNextAction.StartInitialSync,
        true)]
    [InlineData(
        UserLicenseState.Active,
        true,
        ConnectionFixture.AuthorizedInProgress,
        OnboardingState.InitialSyncInProgress,
        OnboardingNextAction.WaitForInitialSync,
        true)]
    [InlineData(
        UserLicenseState.Active,
        true,
        ConnectionFixture.AuthorizedCompleted,
        OnboardingState.Active,
        OnboardingNextAction.None,
        true)]
    public async Task StateIsDerivedFromAuthoritativeLicenseProfileAndCalendarState(
        UserLicenseState licenseState,
        bool hasProfile,
        ConnectionFixture connection,
        OnboardingState expected,
        OnboardingNextAction expectedNextAction,
        bool hasActiveLicense)
    {
        OnboardingStateService service = new(
            new StubLicenseStore(licenseState),
            new StubProfileStore(hasProfile),
            new StubConnectionStore(ConnectionOf(connection)));

        OnboardingSnapshot result = await service.GetAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(expected, result.State);
        Assert.Equal(expectedNextAction, result.NextAction);
        Assert.Equal(hasActiveLicense, result.HasActiveLicense);
    }

    private static GoogleCalendarConnectionView? ConnectionOf(ConnectionFixture fixture) => fixture switch
    {
        ConnectionFixture.None => null,
        ConnectionFixture.NeedsReauthorization => View(
            GoogleCalendarConnectionStatus.NeedsReauthorization,
            GoogleCalendarInitialSyncState.Pending),
        ConnectionFixture.AuthorizedPending => View(
            GoogleCalendarConnectionStatus.Authorized,
            GoogleCalendarInitialSyncState.Pending),
        ConnectionFixture.AuthorizedInProgress => View(
            GoogleCalendarConnectionStatus.Authorized,
            GoogleCalendarInitialSyncState.InProgress),
        ConnectionFixture.AuthorizedCompleted => View(
            GoogleCalendarConnectionStatus.Authorized,
            GoogleCalendarInitialSyncState.Completed),
        _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, null),
    };

    private static GoogleCalendarConnectionView View(
        GoogleCalendarConnectionStatus status,
        GoogleCalendarInitialSyncState initialSyncState) => new()
        {
            UserId = Guid.NewGuid(),
            GrantedScopes = "https://www.googleapis.com/auth/calendar.app.created",
            Status = status,
            InitialSyncState = initialSyncState,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };

    private sealed class StubConnectionStore(GoogleCalendarConnectionView? connection)
        : IGoogleCalendarConnectionStore
    {
        public Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(connection);

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
}
