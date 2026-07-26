using Sirkadiyen.Infrastructure.Google;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class GoogleSourceCredentialFactoryTests
{
    [Fact]
    public void TheSourceCredentialAsksForReadOnlyAccessToBothPublicationSurfaces()
    {
        // One credential polls every source, whichever way its program is
        // published (ADR-083). Both scopes are read-only: nothing in the
        // acquisition path may write to a source document.
        Assert.Equal(
            [
                "https://www.googleapis.com/auth/spreadsheets.readonly",
                "https://www.googleapis.com/auth/drive.readonly",
            ],
            GoogleSourceCredentialFactory.Scopes);
    }

    [Fact]
    public void ARefreshTokenCredentialIsBuiltWithoutNetworkAccess()
    {
        GoogleSourceCredentialFactory factory = new();

        Assert.NotNull(factory.Create(new GoogleSourceAccessOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            SourceRefreshToken = "refresh-token",
        }));
    }
}
