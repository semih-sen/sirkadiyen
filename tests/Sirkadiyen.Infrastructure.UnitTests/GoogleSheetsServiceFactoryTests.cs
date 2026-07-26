using Google.Apis.Sheets.v4;
using Sirkadiyen.Infrastructure.Google;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class GoogleSheetsServiceFactoryTests
{
    private readonly GoogleSheetsServiceFactory _factory = new(new GoogleSourceCredentialFactory());

    [Fact]
    public void CreateBuildsReadOnlyServiceFromRefreshTokenWithoutNetworkAccess()
    {
        using SheetsService service = _factory.Create(new GoogleSourceAccessOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            SourceRefreshToken = "refresh-token",
        });

        Assert.Equal("Sirkadiyen.Worker", service.ApplicationName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CreateRequiresExactlyOneCredentialMode(
        bool configureRefreshToken,
        bool configureServiceAccount)
    {
        GoogleSourceAccessOptions options = new()
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            SourceRefreshToken = configureRefreshToken ? "refresh-token" : null,
            ServiceAccountCredentialPath = configureServiceAccount ? "missing.json" : null,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            _factory.Create(options));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }
}
