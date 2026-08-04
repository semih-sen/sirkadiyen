using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Infrastructure.Security;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class DataProtectionAuditIpProtectorTests
{
    private const string Ip = "203.0.113.42";

    [Fact]
    public void AProtectedIpRoundTrips()
    {
        DataProtectionAuditIpProtector protector = Create();

        Assert.Equal(Ip, protector.Unprotect(protector.Protect(Ip)));
    }

    [Fact]
    public void TheCiphertextDoesNotRevealTheIp()
    {
        DataProtectionAuditIpProtector protector = Create();

        string ciphertext = protector.Protect(Ip);

        Assert.DoesNotContain(Ip, ciphertext, StringComparison.Ordinal);
    }

    [Fact]
    public void APayloadProtectedForAnotherPurposeCannotBeUnprotectedHere()
    {
        IDataProtectionProvider provider = Provider();
        string foreign = provider.CreateProtector("Sirkadiyen.GoogleCalendar.RefreshToken.v1")
            .Protect(Ip);

        Assert.ThrowsAny<Exception>(
            () => new DataProtectionAuditIpProtector(provider).Unprotect(foreign));
    }

    private static DataProtectionAuditIpProtector Create() => new(Provider());

    private static IDataProtectionProvider Provider() =>
        new ServiceCollection()
            .AddDataProtection()
            .UseEphemeralDataProtectionProvider()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();
}
