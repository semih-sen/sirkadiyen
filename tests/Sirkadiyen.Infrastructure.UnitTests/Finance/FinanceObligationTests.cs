using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinanceObligationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void CreateRejectsACategoryThatDoesNotMatchTheDirection()
    {
        Assert.Throws<ArgumentException>(() => FinanceObligation.Create(
            FinanceObligationDirection.Receivable,
            FinanceCategory.Servers,
            "A Corp",
            null,
            100m,
            Today,
            null,
            Actor,
            "admin@example.com",
            Now));
    }

    [Fact]
    public void CreateRejectsADueDateBeforeTheIssueDate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceObligation.Create(
            FinanceObligationDirection.Receivable,
            FinanceCategory.LicenseSales,
            "A Corp",
            null,
            100m,
            Today,
            Today.AddDays(-1),
            Actor,
            "admin@example.com",
            Now));
    }

    [Fact]
    public void PartialSettlementMovesToPartiallySettled()
    {
        FinanceObligation obligation = CreateReceivable(100m);

        obligation.RecordSettlement(40m, Now.AddDays(1));

        Assert.Equal(FinanceObligationStatus.PartiallySettled, obligation.Status);
        Assert.Equal(40m, obligation.SettledAmount);
        Assert.Equal(60m, obligation.RemainingAmount);
    }

    [Fact]
    public void SettlingTheFullRemainingAmountMovesToSettled()
    {
        FinanceObligation obligation = CreateReceivable(100m);

        obligation.RecordSettlement(40m, Now.AddDays(1));
        obligation.RecordSettlement(60m, Now.AddDays(2));

        Assert.Equal(FinanceObligationStatus.Settled, obligation.Status);
        Assert.Equal(100m, obligation.SettledAmount);
        Assert.Equal(0m, obligation.RemainingAmount);
    }

    [Fact]
    public void OverSettlementIsRejected()
    {
        FinanceObligation obligation = CreateReceivable(100m);
        obligation.RecordSettlement(80m, Now.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => obligation.RecordSettlement(30m, Now.AddDays(2)));
    }

    [Fact]
    public void CancellingASettlementFromSettledMovesToPartiallySettled()
    {
        FinanceObligation obligation = CreateReceivable(100m);
        obligation.RecordSettlement(100m, Now.AddDays(1));

        obligation.CancelSettlement(40m, Now.AddDays(2));

        Assert.Equal(FinanceObligationStatus.PartiallySettled, obligation.Status);
        Assert.Equal(60m, obligation.SettledAmount);
    }

    [Fact]
    public void CancellingTheFullSettlementReturnsToOpen()
    {
        FinanceObligation obligation = CreateReceivable(100m);
        obligation.RecordSettlement(100m, Now.AddDays(1));

        obligation.CancelSettlement(100m, Now.AddDays(2));

        Assert.Equal(FinanceObligationStatus.Open, obligation.Status);
        Assert.Equal(0m, obligation.SettledAmount);
    }

    [Fact]
    public void CancellingMoreThanWasSettledIsRejected()
    {
        FinanceObligation obligation = CreateReceivable(100m);
        obligation.RecordSettlement(40m, Now.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => obligation.CancelSettlement(50m, Now.AddDays(2)));
    }

    [Fact]
    public void CancellingAnObligationWithANonZeroSettlementIsRejected()
    {
        FinanceObligation obligation = CreateReceivable(100m);
        obligation.RecordSettlement(10m, Now.AddDays(1));

        Assert.Throws<InvalidOperationException>(
            () => obligation.Cancel("No longer valid.", Today.AddDays(2), Now.AddDays(2)));
    }

    [Fact]
    public void SettlingAWrittenOffObligationIsRejected()
    {
        FinanceObligation obligation = CreateReceivable(100m);
        obligation.WriteOff("Uncollectible.", Today.AddDays(30), Now.AddDays(30));

        Assert.Throws<InvalidOperationException>(() => obligation.RecordSettlement(10m, Now.AddDays(31)));
    }

    [Fact]
    public void WriteOffRequiresTheObligationNotAlreadyClosed()
    {
        FinanceObligation obligation = CreateReceivable(100m);
        obligation.WriteOff("Uncollectible.", Today.AddDays(30), Now.AddDays(30));

        Assert.Throws<InvalidOperationException>(
            () => obligation.WriteOff("Again.", Today.AddDays(31), Now.AddDays(31)));
    }

    private static FinanceObligation CreateReceivable(decimal amount) => FinanceObligation.Create(
        FinanceObligationDirection.Receivable,
        FinanceCategory.LicenseSales,
        "A Corp",
        null,
        amount,
        Today,
        null,
        Actor,
        "admin@example.com",
        Now);
}
