namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// The OAuth client Sirkadiyen exchanges Calendar authorization codes with.
/// </summary>
/// <remarks>
/// Separate from <see cref="GoogleSourceAccessOptions"/>, which is the unattended
/// credential for reading schedule sources. This one acts on behalf of a signed-in
/// student and may reuse the browser sign-in client, but it additionally needs the
/// client secret, which never leaves the server.
/// </remarks>
public sealed record GoogleCalendarAuthorizationOptions
{
    /// <summary>
    /// Google requires this literal redirect URI when the authorization code was
    /// obtained by the browser's popup code flow rather than a server redirect.
    /// </summary>
    public const string PostMessageRedirectUri = "postmessage";

    /// <summary>
    /// Grants access only to calendars this application itself creates, which is exactly
    /// the dedicated-calendar model of ADR-024 and never reaches the primary calendar.
    /// </summary>
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar.app.created";

    /// <summary>
    /// Allows listing calendar metadata read-only so an app-created calendar can be found
    /// after a crash between Google creation and local persistence. It grants no event access.
    /// </summary>
    public const string CalendarListReadOnlyScope =
        "https://www.googleapis.com/auth/calendar.calendarlist.readonly";

    public static IReadOnlyList<string> RequiredScopes { get; } =
        [CalendarScope, CalendarListReadOnlyScope];

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public string RedirectUri { get; init; } = PostMessageRedirectUri;
}
