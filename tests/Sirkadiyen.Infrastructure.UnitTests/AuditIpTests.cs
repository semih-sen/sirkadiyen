using Sirkadiyen.Application.Auditing;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class AuditIpTests
{
    [Theory]
    [InlineData("203.0.113.42", "203.0.113.0")]
    [InlineData("198.51.100.255", "198.51.100.0")]
    [InlineData("2001:db8:abcd:1234:5678:9abc:def0:1234", "2001:db8:abcd::")]
    public void MaskClearsHostBits(string input, string expected) =>
        Assert.Equal(expected, AuditIp.Mask(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    public void MaskReturnsNullForMissingOrInvalidAddresses(string? input) =>
        Assert.Null(AuditIp.Mask(input));
}
