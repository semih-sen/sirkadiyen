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
    /// <summary>The scope the product requires to manage its own calendar.</summary>
    string RequiredScope { get; }

    /// <summary>The browser OAuth client ID the frontend starts the consent with.</summary>
    string ClientId { get; }

    /// <exception cref="GoogleCalendarAuthorizationException">
    /// The code was rejected, already used, or Google returned no refresh token.
    /// </exception>
    Task<CalendarAuthorizationTokens> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        CancellationToken cancellationToken);
}

/// <summary>What a successful authorization-code exchange yields.</summary>
public sealed record CalendarAuthorizationTokens
{
    /// <summary>
    /// The long-lived refresh token. Google only returns one when offline access was
    /// requested and the user has not already granted it silently, so its absence is an
    /// error the client raises rather than an empty value it passes on.
    /// </summary>
    public required string RefreshToken { get; init; }

    /// <summary>The scopes Google reports as granted, space-delimited.</summary>
    public required string GrantedScopes { get; init; }
}

/// <summary>A Google authorization-code exchange that cannot yield a usable credential.</summary>
public sealed class GoogleCalendarAuthorizationException : Exception
{
    public GoogleCalendarAuthorizationException(string message)
        : base(message)
    {
    }

    public GoogleCalendarAuthorizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public GoogleCalendarAuthorizationException()
    {
    }
}
