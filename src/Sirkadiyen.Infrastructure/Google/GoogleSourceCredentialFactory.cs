using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Sheets.v4;

namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// Builds the one unattended credential the worker reads schedule sources with.
/// </summary>
/// <remarks>
/// <para>
/// One credential, not one per API. The sources are the same documents whichever
/// way they are published — a program may be a sheet this year and a Drive file
/// the next — and two credentials would mean two grants to keep alive and two
/// ways for polling to half-work.
/// </para>
/// <para>
/// The Drive scope is read-only over the whole Drive the credential can see,
/// because Drive has no narrower scope that can download a file somebody else
/// shared. Service-account mode is therefore the least-privilege mode in
/// practice: the account sees exactly the documents Student Affairs shared with
/// it, and the scope is bounded by that (ADR-083).
/// </para>
/// </remarks>
public sealed class GoogleSourceCredentialFactory
{
    private const string SourceCredentialUserId = "schedule-source";

    /// <summary>Reads Drive file metadata and downloads file content.</summary>
    public const string DriveReadonlyScope = "https://www.googleapis.com/auth/drive.readonly";

    /// <summary>The scopes unattended source acquisition needs, and no others.</summary>
    public static readonly IReadOnlyList<string> Scopes =
    [
        SheetsService.Scope.SpreadsheetsReadonly,
        DriveReadonlyScope,
    ];

    public ICredential Create(GoogleSourceAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        bool hasServiceAccount = !string.IsNullOrWhiteSpace(
            options.ServiceAccountCredentialPath);
        bool hasRefreshToken = !string.IsNullOrWhiteSpace(options.SourceRefreshToken);
        if (hasServiceAccount == hasRefreshToken)
        {
            throw new InvalidOperationException(
                "Configure exactly one Google source credential mode: service account "
                + "or OAuth refresh token.");
        }

        return hasServiceAccount
            ? CreateServiceAccountCredential(options.ServiceAccountCredentialPath!)
            : CreateUserCredential(options);
    }

    private static GoogleCredential CreateServiceAccountCredential(string credentialPath)
    {
        if (!File.Exists(credentialPath))
        {
            throw new InvalidOperationException(
                "The configured Google service-account credential file does not exist.");
        }

        return CredentialFactory.FromFile<ServiceAccountCredential>(credentialPath)
            .ToGoogleCredential()
            .CreateScoped(Scopes);
    }

    /// <summary>
    /// Rebuilds a credential around an already-granted refresh token.
    /// </summary>
    /// <remarks>
    /// The scope list here describes what the grant is expected to carry; it does
    /// not extend one. A refresh token minted before Drive acquisition existed
    /// holds the Sheets scope alone, and Drive will answer 403 until the grant is
    /// re-issued with both scopes.
    /// </remarks>
    private static UserCredential CreateUserCredential(GoogleSourceAccessOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "OAuth source access requires client ID, client secret, and refresh token.");
        }

        GoogleAuthorizationCodeFlow flow = new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
            },
            Scopes = Scopes,
        });
        TokenResponse token = new() { RefreshToken = options.SourceRefreshToken };
        return new UserCredential(flow, SourceCredentialUserId, token);
    }
}
