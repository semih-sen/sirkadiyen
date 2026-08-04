using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinanceAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public void OpenCreatesAnActiveAccountInTry()
    {
        FinanceAccount account = FinanceAccount.Open(
            Guid.NewGuid(),
            "Main cash box",
            FinanceAccountKind.Cash,
            Today,
            Now);

        Assert.Equal(FinanceAccountStatus.Active, account.Status);
        Assert.Equal(FinanceAccount.SupportedCurrencyCode, account.CurrencyCode);
        Assert.Null(account.ClosedAtUtc);
    }

    [Fact]
    public void CloseRequiresAReasonAndSetsClosureFields()
    {
        FinanceAccount account = FinanceAccount.Open(
            Guid.NewGuid(),
            "Main cash box",
            FinanceAccountKind.Cash,
            Today,
            Now);

        account.Close("No longer used.", Now.AddDays(1));

        Assert.Equal(FinanceAccountStatus.Closed, account.Status);
        Assert.Equal("No longer used.", account.ClosedReason);
        Assert.Equal(Now.AddDays(1), account.ClosedAtUtc);
    }

    [Fact]
    public void ClosingAnAlreadyClosedAccountIsRejected()
    {
        FinanceAccount account = FinanceAccount.Open(
            Guid.NewGuid(),
            "Main cash box",
            FinanceAccountKind.Cash,
            Today,
            Now);
        account.Close("No longer used.", Now.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => account.Close("Again.", Now.AddDays(2)));
    }
}
