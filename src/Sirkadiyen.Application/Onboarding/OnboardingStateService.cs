using Sirkadiyen.Application.Licensing;

namespace Sirkadiyen.Application.Onboarding;

/// <summary>
/// Derives resumable onboarding state from authoritative backend records.
/// </summary>
public sealed class OnboardingStateService(ILicenseStore licenseStore)
{
    public async Task<OnboardingSnapshot> GetAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        UserLicenseState licenseState = await licenseStore.GetUserLicenseStateAsync(
            userId,
            cancellationToken);

        return licenseState switch
        {
            UserLicenseState.None => Snapshot(
                OnboardingState.LicenseRequired,
                hasActiveLicense: false,
                OnboardingNextAction.RedeemLicense),
            UserLicenseState.Suspended => Snapshot(
                OnboardingState.Suspended,
                hasActiveLicense: false,
                OnboardingNextAction.ContactSupport),
            UserLicenseState.Active => Snapshot(
                OnboardingState.ProfileRequired,
                hasActiveLicense: true,
                OnboardingNextAction.CompleteAcademicProfile),
            _ => throw new ArgumentOutOfRangeException(
                nameof(licenseState),
                licenseState,
                "Unknown user license state."),
        };
    }

    private static OnboardingSnapshot Snapshot(
        OnboardingState state,
        bool hasActiveLicense,
        OnboardingNextAction nextAction) => new()
        {
            State = state,
            HasActiveLicense = hasActiveLicense,
            NextAction = nextAction,
        };
}

public sealed record OnboardingSnapshot
{
    public required OnboardingState State { get; init; }

    public required bool HasActiveLicense { get; init; }

    public required OnboardingNextAction NextAction { get; init; }
}

public enum OnboardingState
{
    LicenseRequired,
    ProfileRequired,
    CalendarAuthorizationRequired,
    ReadyForInitialSync,
    InitialSyncInProgress,
    Active,
    ActionRequired,
    Suspended,
}

public enum OnboardingNextAction
{
    RedeemLicense,
    CompleteAcademicProfile,
    AuthorizeCalendar,
    StartInitialSync,
    WaitForInitialSync,
    None,
    ResolveAction,
    ContactSupport,
}
