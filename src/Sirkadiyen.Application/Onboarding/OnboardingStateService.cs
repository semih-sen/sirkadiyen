using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;

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
    /// An activated account still needs an academic profile, then a Calendar authorization,
    /// then its one-time initial synchronization, before it is fully active. Each step is
    /// derived from an authoritative record, so an interrupted onboarding resumes at the right
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

        GoogleCalendarConnectionView? connection = await connectionStore.GetByUserIdAsync(
            userId,
            cancellationToken);

        // A missing connection, or one that needs re-authorization, does not count as
        // authorized: a revoked grant sends the user back to consent instead of stalling in a
        // state that cannot synchronize.
        if (connection is null
            || connection.Status != GoogleCalendarConnectionStatus.Authorized)
        {
            return Snapshot(
                OnboardingState.CalendarAuthorizationRequired,
                hasActiveLicense: true,
                OnboardingNextAction.AuthorizeCalendar);
        }

        // Authorized: the remaining step is the initial synchronization, whose progress the
        // connection records (ADR-058).
        return connection.InitialSyncState switch
        {
            GoogleCalendarInitialSyncState.Pending => Snapshot(
                OnboardingState.ReadyForInitialSync,
                hasActiveLicense: true,
                OnboardingNextAction.StartInitialSync),
            GoogleCalendarInitialSyncState.InProgress => Snapshot(
                OnboardingState.InitialSyncInProgress,
                hasActiveLicense: true,
                OnboardingNextAction.WaitForInitialSync),
            GoogleCalendarInitialSyncState.Completed => Snapshot(
                OnboardingState.Active,
                hasActiveLicense: true,
                OnboardingNextAction.None),
            _ => throw new ArgumentOutOfRangeException(
                nameof(userId),
                connection.InitialSyncState,
                "Unknown initial synchronization state."),
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
