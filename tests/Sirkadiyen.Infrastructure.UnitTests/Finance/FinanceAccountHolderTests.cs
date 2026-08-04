using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinanceAccountHolderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateWithAPositiveShareIsAnEligiblePartner()
    {
        FinanceAccountHolder holder = FinanceAccountHolder.Create("Ada", null, 5000, Now);

        Assert.True(holder.IsEligiblePartner);
    }

    [Fact]
    public void CreateWithZeroShareIsNotAPartner()
    {
        FinanceAccountHolder holder = FinanceAccountHolder.Create("Ada", null, 0, Now);

        Assert.False(holder.IsEligiblePartner);
    }

    [Fact]
    public void CreateRejectsAShareAboveTenThousand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FinanceAccountHolder.Create("Ada", null, 10_001, Now));
    }

    [Fact]
    public void DeactivateZeroesTheShareAndMakesTheHolderNotAPartner()
    {
        FinanceAccountHolder holder = FinanceAccountHolder.Create("Ada", null, 5000, Now);

        holder.Deactivate(Now.AddMinutes(1));

        Assert.Equal(FinanceAccountHolderStatus.Inactive, holder.Status);
        Assert.Equal(0, holder.ShareBasisPoints);
        Assert.False(holder.IsEligiblePartner);
    }

    [Fact]
    public void DeactivatingAnAlreadyInactiveHolderIsRejected()
    {
        FinanceAccountHolder holder = FinanceAccountHolder.Create("Ada", null, 5000, Now);
        holder.Deactivate(Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => holder.Deactivate(Now.AddMinutes(2)));
    }

    [Fact]
    public void SetShareOnAnInactiveHolderIsRejected()
    {
        FinanceAccountHolder holder = FinanceAccountHolder.Create("Ada", null, 0, Now);
        holder.Deactivate(Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => holder.SetShare(5000, Now.AddMinutes(2)));
    }
}
