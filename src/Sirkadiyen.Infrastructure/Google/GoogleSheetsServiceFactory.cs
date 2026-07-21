using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace Sirkadiyen.Infrastructure.Google;

public sealed class GoogleSheetsServiceFactory
{
    private const string ApplicationName = "Sirkadiyen.Worker";
    private const string SourceCredentialUserId = "schedule-source";

    public SheetsService Create(GoogleSourceAccessOptions options)
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

        IConfigurableHttpClientInitializer credential = hasServiceAccount
            ? CreateServiceAccountCredential(options.ServiceAccountCredentialPath!)
            : CreateUserCredential(options);

        return new SheetsService(new BaseClientService.Initializer
        {
            ApplicationName = ApplicationName,
            HttpClientInitializer = credential,
        });
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
            .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
    }

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
            Scopes = [SheetsService.Scope.SpreadsheetsReadonly],
        });
        TokenResponse token = new() { RefreshToken = options.SourceRefreshToken };
        return new UserCredential(flow, SourceCredentialUserId, token);
    }
}
