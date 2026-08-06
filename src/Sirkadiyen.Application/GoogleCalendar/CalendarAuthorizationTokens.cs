namespace Sirkadiyen.Application.GoogleCalendar;

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
