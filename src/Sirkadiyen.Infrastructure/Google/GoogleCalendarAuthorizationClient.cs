using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Sirkadiyen.Application.GoogleCalendar;

namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// Exchanges a Google authorization code for a refresh token using the configured
/// confidential OAuth client.
/// </summary>
/// <remarks>
/// The flow is built once and reused: an authorization is rare, but constructing one per
/// call would create a new <c>HttpClient</c> each time. No token data store is configured,
/// so the exchanged credential is returned to the caller and never written to disk by the
/// Google library; persistence is Sirkadiyen's own encrypted store (ADR-057).
/// </remarks>
public sealed class GoogleCalendarAuthorizationClient
    : IGoogleCalendarAuthorizationClient, IDisposable
{
    /// <summary>
    /// The Google library keys its (here absent) token store by user. The exchange is
    /// stateless for us, so a constant stands in for that key.
    /// </summary>
    private const string ExchangeUserKey = "calendar-authorization";

    private readonly GoogleCalendarAuthorizationOptions options;
    private readonly GoogleAuthorizationCodeFlow flow;

    public GoogleCalendarAuthorizationClient(GoogleCalendarAuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.options = options;
        flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
            },
            Scopes = GoogleCalendarAuthorizationOptions.RequiredScopes,
        });
    }

    public IReadOnlyList<string> RequiredScopes =>
        GoogleCalendarAuthorizationOptions.RequiredScopes;

    public string ClientId => options.ClientId;

    public async Task<CalendarAuthorizationTokens> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);

        TokenResponse token;
        try
        {
            token = await flow.ExchangeCodeForTokenAsync(
                ExchangeUserKey,
                authorizationCode,
                options.RedirectUri,
                cancellationToken);
        }
        catch (TokenResponseException exception)
        {
            throw new GoogleCalendarAuthorizationException(
                "Google rejected the authorization code.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new GoogleCalendarAuthorizationException(
                "The Google token endpoint could not be reached.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            // Without a refresh token there is nothing to synchronize with once the
            // access token expires, so this is a failed authorization rather than a
            // partial success worth storing.
            throw new GoogleCalendarAuthorizationException(
                "Google returned no refresh token; offline access was not granted.");
        }

        return new CalendarAuthorizationTokens
        {
            RefreshToken = token.RefreshToken,
            GrantedScopes = token.Scope ?? string.Empty,
        };
    }

    public async Task<bool> RevokeRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        try
        {
            // Reuses the flow's HTTP client to call Google's revocation endpoint. A refresh token
            // revokes the whole grant. This is best-effort account cleanup (ADR-118): a token Google
            // already considers invalid is reported as revoked rather than an error.
            await flow.RevokeTokenAsync(ExchangeUserKey, refreshToken, cancellationToken);
            return true;
        }
        catch (TokenResponseException)
        {
            // Google returns a 400 with "invalid_token" for a token that is already unusable, which
            // is the outcome we wanted: there is no live grant left to revoke.
            return true;
        }
        catch (HttpRequestException)
        {
            // The revocation endpoint could not be reached; the grant may still be live. The caller
            // records this as "not revoked" rather than failing the whole deletion.
            return false;
        }
    }

    public void Dispose() => flow.Dispose();
}
