using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Infrastructure.Licensing;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class LicenseCodeServiceTests
{
    private readonly LicenseCodeService service = new(new LicenseCodeOptions
    {
        HashKey = Enumerable.Range(0, 32).Select(Convert.ToByte).ToArray(),
    });

    [Fact]
    public void GeneratedCodeHasExpectedFormatAndRoundTripsToSameHash()
    {
        GeneratedLicenseCode generated = service.Generate();

        Assert.Matches(
            "^SRK-[A-HJ-NP-Z2-9]{5}-[A-HJ-NP-Z2-9]{5}$",
            generated.PlaintextCode);
        Assert.True(
            service.TryHash(generated.PlaintextCode.ToLowerInvariant(), out byte[] normalized));
        Assert.Equal(generated.CodeHash, normalized);
        Assert.Equal(32, generated.CodeHash.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SRK-SHORT")]
    [InlineData("SRK-OOOOO-OOOOO")]
    [InlineData("NOTSRK-AAAAA-AAAAA")]
    public void MalformedCodeIsRejectedWithoutAHash(string code)
    {
        Assert.False(service.TryHash(code, out byte[] hash));
        Assert.Empty(hash);
    }

    [Fact]
    public void PreviouslyGeneratedLongCodeRemainsRedeemable()
    {
        Assert.True(
            service.TryHash(
                "SIRK-AAAAA-AAAAA-AAAAA-AAAAA",
                out byte[] legacyHash));
        Assert.Equal(32, legacyHash.Length);
    }

    [Fact]
    public void DifferentHashKeysDoNotProduceTheSameLookupValue()
    {
        GeneratedLicenseCode generated = service.Generate();
        LicenseCodeService other = new(new LicenseCodeOptions
        {
            HashKey = Enumerable.Repeat((byte)42, 32).ToArray(),
        });

        Assert.True(other.TryHash(generated.PlaintextCode, out byte[] otherHash));
        Assert.NotEqual(generated.CodeHash, otherHash);
    }
}
