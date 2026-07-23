using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Domain.Licensing;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class OnboardingStateServiceTests
{
    [Theory]
    [InlineData(
        UserLicenseState.None,
        OnboardingState.LicenseRequired,
        OnboardingNextAction.RedeemLicense,
        false)]
    [InlineData(
        UserLicenseState.Active,
        OnboardingState.ProfileRequired,
        OnboardingNextAction.CompleteAcademicProfile,
        true)]
    [InlineData(
        UserLicenseState.Suspended,
        OnboardingState.Suspended,
        OnboardingNextAction.ContactSupport,
        false)]
    public async Task StateIsDerivedFromAuthoritativeLicenseState(
        UserLicenseState licenseState,
        OnboardingState expected,
        OnboardingNextAction expectedNextAction,
        bool hasActiveLicense)
    {
        OnboardingStateService service = new(new StubLicenseStore(licenseState));

        OnboardingSnapshot result = await service.GetAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(expected, result.State);
        Assert.Equal(expectedNextAction, result.NextAction);
        Assert.Equal(hasActiveLicense, result.HasActiveLicense);
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
