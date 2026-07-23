using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Infrastructure.Security;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class DataProtectionCalendarTokenProtectorTests
{
    private const string Token = "1//0gGoogleRefreshTokenExample_abcdef";

    [Fact]
    public void AProtectedTokenRoundTrips()
    {
        DataProtectionCalendarTokenProtector protector = Create();

        string ciphertext = protector.Protect(Token);

        Assert.Equal(Token, protector.Unprotect(ciphertext));
    }

    [Fact]
    public void TheCiphertextDoesNotRevealTheToken()
    {
        DataProtectionCalendarTokenProtector protector = Create();

        string ciphertext = protector.Protect(Token);

        Assert.NotEqual(Token, ciphertext);
        Assert.DoesNotContain(Token, ciphertext, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtectingTwiceDoesNotProduceTheSameCiphertext()
    {
        DataProtectionCalendarTokenProtector protector = Create();

        // Data Protection is randomized, so identical tokens must not be correlatable
        // by comparing stored values across users.
        Assert.NotEqual(protector.Protect(Token), protector.Protect(Token));
    }

    [Fact]
    public void APayloadProtectedForAnotherPurposeCannotBeUnprotectedHere()
    {
        IDataProtectionProvider provider = Provider();
        string foreign = provider.CreateProtector("some.other.purpose").Protect(Token);

        Assert.ThrowsAny<Exception>(
            () => new DataProtectionCalendarTokenProtector(provider).Unprotect(foreign));
    }

    [Fact]
    public void ABlankValueIsRejected()
    {
        DataProtectionCalendarTokenProtector protector = Create();

        Assert.Throws<ArgumentException>(() => protector.Protect("   "));
        Assert.Throws<ArgumentException>(() => protector.Unprotect("   "));
    }

    private static DataProtectionCalendarTokenProtector Create() => new(Provider());

    /// <summary>
    /// An in-memory key ring, so the tests neither read nor write a real key ring on the
    /// machine running them. Each call produces an independent one.
    /// </summary>
    private static IDataProtectionProvider Provider() =>
        new ServiceCollection()
            .AddDataProtection()
            .UseEphemeralDataProtectionProvider()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();
}
