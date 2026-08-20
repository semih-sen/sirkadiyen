using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;

namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// Best-effort removal of a deleted account's Google footprint: its managed calendar and the stored
/// refresh-token grant (ADR-118).
/// </summary>
/// <remarks>
/// It lives here, not in the use case, because it owns the two things the application layer should
/// not: the plaintext credential (decrypted only for the duration of the call) and the provider
/// exceptions. Every failure is logged and folded into the returned outcome; none is rethrown, so a
/// person's local erasure never depends on Google being reachable. What could not be done is visible
/// both in this log and in the deletion's audit metadata.
/// </remarks>
public sealed class ExternalAccountCleanupService(
    ICalendarTokenProtector tokenProtector,
    IUserCalendarClient calendarClient,
    IGoogleCalendarAuthorizationClient authorizationClient,
    ILogger<ExternalAccountCleanupService> logger) : IExternalAccountCleanup
{
    public async Task<ExternalAccountCleanupResult> CleanUpAsync(
        AccountCalendarCleanup credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);

        bool hadCalendar = credential.ManagedCalendarId is not null;

        string refreshToken;
        try
        {
            refreshToken = tokenProtector.Unprotect(credential.ProtectedRefreshToken);
        }
        catch (Exception exception)
        {
            // A credential that will not decrypt (a rotated key ring, ADR-057) cannot reach Google
            // at all. Nothing external can be cleaned up; the local erasure still proceeds.
            logger.LogWarning(
                exception,
                "Could not decrypt a stored Calendar credential during account deletion; skipping "
                    + "Google cleanup.");
            return new ExternalAccountCleanupResult
            {
                HadManagedCalendar = hadCalendar,
                CalendarDeleted = false,
                TokenRevoked = false,
            };
        }

        var access = new CalendarAccess { RefreshToken = refreshToken };

        bool calendarDeleted = false;
        if (credential.ManagedCalendarId is not null)
        {
            try
            {
                CalendarContainerDeleteOutcome outcome = await calendarClient
                    .DeleteManagedCalendarAsync(
                        access,
                        credential.ManagedCalendarId,
                        cancellationToken);
                calendarDeleted = outcome is CalendarContainerDeleteOutcome.Deleted;
            }
            catch (Exception exception)
            {
                // Deliberately broad: this is a best-effort courtesy, and erasure is a right that
                // must not depend on Google being reachable or the credential being usable. Any
                // failure — a classified sync error, an auth/credential build failure, a raw HTTP or
                // Google API exception — is logged and the local deletion proceeds regardless
                // (ADR-118). What was not done is recorded in the deletion's audit metadata.
                logger.LogWarning(
                    exception,
                    "Could not delete a managed calendar during account deletion; the account will "
                        + "still be erased locally.");
            }
        }

        bool tokenRevoked = false;
        try
        {
            // The revoke reports reachability as its return value, but building the request or the
            // credential can still throw; the same best-effort rule applies.
            tokenRevoked = await authorizationClient
                .RevokeRefreshTokenAsync(refreshToken, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not revoke a Google grant during account deletion; the account will still be "
                    + "erased locally.");
        }

        return new ExternalAccountCleanupResult
        {
            HadManagedCalendar = hadCalendar,
            CalendarDeleted = calendarDeleted,
            TokenRevoked = tokenRevoked,
        };
    }
}
