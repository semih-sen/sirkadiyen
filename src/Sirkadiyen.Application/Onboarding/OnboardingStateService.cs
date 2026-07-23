using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.StudentProfiles;

namespace Sirkadiyen.Application.Onboarding;

/// <summary>
/// Derives resumable onboarding state from authoritative backend records.
/// </summary>
public sealed class OnboardingStateService(
    ILicenseStore licenseStore,
    IStudentProfileStore profileStore,
    IGoogleCalendarConnectionStore connectionStore)
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
            UserLicenseState.Active => await ActivatedStateAsync(userId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(licenseState),
                licenseState,
                "Unknown user license state."),
        };
    }

    /// <summary>
    /// An activated account still needs an academic profile, and then a Calendar
    /// authorization, before anything can be synchronized. Each step is derived from
    /// whether its record exists, so an interrupted onboarding resumes at the right
    /// place rather than trusting the client to remember how far it got.
    /// </summary>
    private async Task<OnboardingSnapshot> ActivatedStateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        bool hasProfile = await profileStore.ExistsForUserAsync(userId, cancellationToken);
        if (!hasProfile)
        {
            return Snapshot(
                OnboardingState.ProfileRequired,
                hasActiveLicense: true,
                OnboardingNextAction.CompleteAcademicProfile);
        }

        // A connection that needs re-authorization does not count as authorized, so a
        // revoked grant sends the user back to consent instead of stalling in a state
        // that cannot synchronize.
        bool hasCalendarAuthorization = await connectionStore.IsAuthorizedForUserAsync(
            userId,
            cancellationToken);

        return hasCalendarAuthorization
            ? Snapshot(
                OnboardingState.ReadyForInitialSync,
                hasActiveLicense: true,
                OnboardingNextAction.StartInitialSync)
            : Snapshot(
                OnboardingState.CalendarAuthorizationRequired,
                hasActiveLicense: true,
                OnboardingNextAction.AuthorizeCalendar);
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
