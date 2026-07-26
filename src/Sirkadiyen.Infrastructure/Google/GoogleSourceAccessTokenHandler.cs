using System.Net.Http.Headers;
using Google.Apis.Auth.OAuth2;

namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// Attaches the unattended source credential's access token to every Drive
/// request.
/// </summary>
/// <remarks>
/// The credential caches and refreshes the token itself, which is why it is a
/// singleton and this handler is not. Keeping the header here means the token is
/// never held by, passed to, or formatted by the client that builds the request,
/// so it cannot reach a log line or an exception message (guideline 15).
/// </remarks>
public sealed class GoogleSourceAccessTokenHandler(ICredential credential) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string token = await credential.GetAccessTokenForRequestAsync(
            cancellationToken: cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
