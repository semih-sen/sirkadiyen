namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Exchanges a one-time Google authorization code for the long-lived credential the
/// synchronization path will use.
/// </summary>
/// <remarks>
/// The exchange carries the OAuth client secret and therefore only ever happens on the
/// server; the browser sends the authorization code and nothing else (ADR-057).
/// </remarks>
public interface IGoogleCalendarAuthorizationClient
{
    /// <summary>
    /// The scopes the product requires to manage its own calendar and recover a calendar
    /// whose creation succeeded before its id could be persisted.
    /// </summary>
    IReadOnlyList<string> RequiredScopes { get; }

    /// <summary>The browser OAuth client ID the frontend starts the consent with.</summary>
    string ClientId { get; }

    /// <exception cref="GoogleCalendarAuthorizationException">
    /// The code was rejected, already used, or Google returned no refresh token.
    /// </exception>
    Task<CalendarAuthorizationTokens> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks Google to revoke a stored refresh token, so the grant Sirkadiyen held does not outlive
    /// the account it belonged to (ADR-118).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if Google accepted the revocation or reported the token already
    /// invalid; <see langword="false"/> if it could not be revoked. Account deletion proceeds
    /// regardless — this is a best-effort cleanup of an external grant, never a gate.
    /// </returns>
    Task<bool> RevokeRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}
