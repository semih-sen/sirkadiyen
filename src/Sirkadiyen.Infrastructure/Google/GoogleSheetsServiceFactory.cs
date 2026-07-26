using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace Sirkadiyen.Infrastructure.Google;

public sealed class GoogleSheetsServiceFactory(GoogleSourceCredentialFactory credentialFactory)
{
    private const string ApplicationName = "Sirkadiyen.Worker";

    public SheetsService Create(GoogleSourceAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The same credential the Drive client uses, so a source account that can
        // poll one transport can poll the other (ADR-083).
        ICredential credential = credentialFactory.Create(options);

        return new SheetsService(new BaseClientService.Initializer
        {
            ApplicationName = ApplicationName,
            HttpClientInitializer = credential,
        });
    }
}
