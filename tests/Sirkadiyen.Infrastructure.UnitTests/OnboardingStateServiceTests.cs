using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class OnboardingStateServiceTests
{
    [Theory]
    [InlineData(
        UserLicenseState.None,
        false,
        false,
        OnboardingState.LicenseRequired,
        OnboardingNextAction.RedeemLicense,
        false)]
    [InlineData(
        UserLicenseState.Suspended,
        false,
        false,
        OnboardingState.Suspended,
        OnboardingNextAction.ContactSupport,
        false)]
    [InlineData(
        UserLicenseState.Active,
        false,
        false,
        OnboardingState.ProfileRequired,
        OnboardingNextAction.CompleteAcademicProfile,
        true)]
    [InlineData(
        UserLicenseState.Active,
        true,
        false,
        OnboardingState.CalendarAuthorizationRequired,
        OnboardingNextAction.AuthorizeCalendar,
        true)]
    [InlineData(
        UserLicenseState.Active,
        true,
        true,
        OnboardingState.ReadyForInitialSync,
        OnboardingNextAction.StartInitialSync,
        true)]
    public async Task StateIsDerivedFromAuthoritativeLicenseProfileAndCalendarState(
        UserLicenseState licenseState,
        bool hasProfile,
        bool hasCalendarAuthorization,
        OnboardingState expected,
        OnboardingNextAction expectedNextAction,
        bool hasActiveLicense)
    {
        OnboardingStateService service = new(
            new StubLicenseStore(licenseState),
            new StubProfileStore(hasProfile),
            new StubConnectionStore(hasCalendarAuthorization));

        OnboardingSnapshot result = await service.GetAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(expected, result.State);
        Assert.Equal(expectedNextAction, result.NextAction);
        Assert.Equal(hasActiveLicense, result.HasActiveLicense);
    }

    private sealed class StubConnectionStore(bool isAuthorized) : IGoogleCalendarConnectionStore
    {
        public Task<bool> IsAuthorizedForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(isAuthorized);

        public Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GoogleCalendarConnectionView> UpsertAuthorizationAsync(
            Guid userId,
            string protectedRefreshToken,
            string grantedScopes,
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
