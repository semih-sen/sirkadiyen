using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.StudentProfiles;

namespace Sirkadiyen.Application.GoogleCalendar;

public sealed record CalendarAuthorizationResult
{
    public required CalendarAuthorizationOutcome Outcome { get; init; }

    public GoogleCalendarConnectionView? Connection { get; init; }
}

public enum CalendarAuthorizationOutcome
{
    Authorized,

    /// <summary>The account is not activated, or has no academic profile yet.</summary>
    PrerequisitesNotMet,

    /// <summary>The user completed consent but withheld the Calendar permission.</summary>
    InsufficientScope,

    /// <summary>Google rejected the code, or returned no long-lived credential.</summary>
    ExchangeFailed,
}

/// <summary>
/// Turns a one-time Google authorization code into a stored, encrypted Calendar
/// authorization for the signed-in user.
/// </summary>
public sealed class CalendarAuthorizationService(
    IGoogleCalendarAuthorizationClient authorizationClient,
    ICalendarTokenProtector tokenProtector,
    IGoogleCalendarConnectionStore connectionStore,
    ILicenseStore licenseStore,
    IStudentProfileStore profileStore,
    TimeProvider timeProvider)
{
    public string RequiredScope => authorizationClient.RequiredScope;

    public string ClientId => authorizationClient.ClientId;

    public Task<GoogleCalendarConnectionView?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        connectionStore.GetByUserIdAsync(userId, cancellationToken);

    public async Task<CalendarAuthorizationResult> AuthorizeAsync(
        Guid userId,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);

        // The onboarding order is enforced by the backend, not the UI (guideline §6, §8):
        // Calendar access is only ever granted by an activated account that has already
        // told us which cohort it belongs to.
        if (!await HasCompletedPrerequisitesAsync(userId, cancellationToken))
        {
            return new CalendarAuthorizationResult
            {
                Outcome = CalendarAuthorizationOutcome.PrerequisitesNotMet,
            };
        }

        CalendarAuthorizationTokens tokens;
        try
        {
            tokens = await authorizationClient.ExchangeAuthorizationCodeAsync(
                authorizationCode,
                cancellationToken);
        }
        catch (GoogleCalendarAuthorizationException)
        {
            // The caller can recover by starting consent again; the reason Google gave
            // is not echoed back to the browser.
            return new CalendarAuthorizationResult
            {
                Outcome = CalendarAuthorizationOutcome.ExchangeFailed,
            };
        }

        // Google reports what was actually granted, which is not necessarily what was
        // asked for: a user can clear the Calendar permission on the consent screen and
        // still complete the flow. Storing that grant would leave onboarding claiming an
        // authorization that cannot synchronize.
        if (!GrantIncludesRequiredScope(tokens.GrantedScopes))
        {
            return new CalendarAuthorizationResult
            {
                Outcome = CalendarAuthorizationOutcome.InsufficientScope,
            };
        }

        string protectedRefreshToken = tokenProtector.Protect(tokens.RefreshToken);
        GoogleCalendarConnectionView stored = await connectionStore.UpsertAuthorizationAsync(
            userId,
            protectedRefreshToken,
            tokens.GrantedScopes,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return new CalendarAuthorizationResult
        {
            Outcome = CalendarAuthorizationOutcome.Authorized,
            Connection = stored,
        };
    }

    private async Task<bool> HasCompletedPrerequisitesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        UserLicenseState licenseState = await licenseStore.GetUserLicenseStateAsync(
            userId,
            cancellationToken);
        if (licenseState != UserLicenseState.Active)
        {
            return false;
        }

        return await profileStore.ExistsForUserAsync(userId, cancellationToken);
    }

    private bool GrantIncludesRequiredScope(string grantedScopes) =>
        grantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(authorizationClient.RequiredScope, StringComparer.Ordinal);
}
