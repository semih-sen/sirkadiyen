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
        OnboardingState.LicenseRequired,
        OnboardingNextAction.RedeemLicense,
        false)]
    [InlineData(
        UserLicenseState.Suspended,
        false,
        OnboardingState.Suspended,
        OnboardingNextAction.ContactSupport,
        false)]
    [InlineData(
        UserLicenseState.Active,
        false,
        OnboardingState.ProfileRequired,
        OnboardingNextAction.CompleteAcademicProfile,
        true)]
    [InlineData(
        UserLicenseState.Active,
        true,
        OnboardingState.CalendarAuthorizationRequired,
        OnboardingNextAction.AuthorizeCalendar,
        true)]
    public async Task StateIsDerivedFromAuthoritativeLicenseAndProfileState(
        UserLicenseState licenseState,
        bool hasProfile,
        OnboardingState expected,
        OnboardingNextAction expectedNextAction,
        bool hasActiveLicense)
    {
        OnboardingStateService service = new(
            new StubLicenseStore(licenseState),
            new StubProfileStore(hasProfile));

        OnboardingSnapshot result = await service.GetAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(expected, result.State);
        Assert.Equal(expectedNextAction, result.NextAction);
        Assert.Equal(hasActiveLicense, result.HasActiveLicense);
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
