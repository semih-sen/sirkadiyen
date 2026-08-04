using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinanceAmountTests
{
    [Fact]
    public void RequireAcceptsATwoDecimalValue()
    {
        Assert.Equal(0.01m, FinanceAmount.Require(0.01m, "amount"));
    }

    [Fact]
    public void RequireRejectsAThreeDecimalValueRatherThanRounding()
    {
        Assert.Throws<ArgumentException>(() => FinanceAmount.Require(1.005m, "amount"));
    }

    [Fact]
    public void RequireRejectsAValueAboveTheMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FinanceAmount.Require(FinanceAmount.MaximumAmount + 0.01m, "amount"));
    }

    [Fact]
    public void RequireAcceptsANegativeValueWithinBounds()
    {
        Assert.Equal(-5.00m, FinanceAmount.Require(-5.00m, "amount"));
    }

    [Fact]
    public void RequirePositiveAcceptsOneKurus()
    {
        Assert.Equal(0.01m, FinanceAmount.RequirePositive(0.01m, "amount"));
    }

    [Fact]
    public void RequirePositiveRejectsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceAmount.RequirePositive(0m, "amount"));
    }

    [Fact]
    public void RequirePositiveRejectsANegativeValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceAmount.RequirePositive(-1m, "amount"));
    }

    [Fact]
    public void RequirePositiveRejectsAThreeDecimalValue()
    {
        Assert.Throws<ArgumentException>(() => FinanceAmount.RequirePositive(1.001m, "amount"));
    }

    [Fact]
    public void RequireNonZeroRejectsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceAmount.RequireNonZero(0m, "amount"));
    }

    [Fact]
    public void RequireNonZeroAcceptsANegativeValue()
    {
        Assert.Equal(-0.01m, FinanceAmount.RequireNonZero(-0.01m, "amount"));
    }
}
