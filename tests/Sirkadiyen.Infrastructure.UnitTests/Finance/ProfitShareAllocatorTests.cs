using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class ProfitShareAllocatorTests
{
    public static IEnumerable<object[]> ExactnessCases()
    {
        yield return [48200.00m, new[] { 3334, 3333, 3333 }];
        yield return [100.00m, new[] { 3333, 3333, 3334 }];
        yield return [0.01m, new[] { 3334, 3333, 3333 }];
        yield return [10.00m, new[] { 1429, 1429, 1429, 1429, 1429, 1429, 1426 }];
        yield return [0.03m, new[] { 3333, 3333, 3334 }];
        yield return [999999999.99m, new[] { 10000 }];
    }

    [Theory]
    [MemberData(nameof(ExactnessCases))]
    public void TheAllocationSumsExactlyToTheDistributableAmount(decimal total, int[] shares)
    {
        IReadOnlyList<ProfitShareInput> partners = ToPartners(shares);

        IReadOnlyList<ProfitShareAllocation> allocations = ProfitShareAllocator.Allocate(total, partners);

        Assert.Equal(total, allocations.Sum(allocation => allocation.AllocatedAmount));
    }

    [Theory]
    [MemberData(nameof(ExactnessCases))]
    public void NoPartnerDeviatesByMoreThanOneMinorUnitFromTheirExactShare(decimal total, int[] shares)
    {
        IReadOnlyList<ProfitShareInput> partners = ToPartners(shares);

        IReadOnlyList<ProfitShareAllocation> allocations = ProfitShareAllocator.Allocate(total, partners);

        foreach (ProfitShareAllocation allocation in allocations)
        {
            decimal exactShare = total * allocation.ShareBasisPoints / 10_000m;
            Assert.True(
                Math.Abs(allocation.AllocatedAmount - exactShare) <= 0.01m,
                $"Holder {allocation.HolderId} deviated by more than 0.01 from its exact share.");
        }
    }

    [Fact]
    public void TheAllocationIsIndependentOfInputOrder()
    {
        var random = new Random(42);
        ProfitShareInput[] basePartners =
        [
            new() { HolderId = Guid.NewGuid(), ShareBasisPoints = 1429 },
            new() { HolderId = Guid.NewGuid(), ShareBasisPoints = 1429 },
            new() { HolderId = Guid.NewGuid(), ShareBasisPoints = 1429 },
            new() { HolderId = Guid.NewGuid(), ShareBasisPoints = 1429 },
            new() { HolderId = Guid.NewGuid(), ShareBasisPoints = 1429 },
            new() { HolderId = Guid.NewGuid(), ShareBasisPoints = 1429 },
            new() { HolderId = Guid.NewGuid(), ShareBasisPoints = 1426 },
        ];

        Dictionary<Guid, decimal> baseline = ProfitShareAllocator
            .Allocate(10.00m, basePartners)
            .ToDictionary(allocation => allocation.HolderId, allocation => allocation.AllocatedAmount);

        for (int shuffle = 0; shuffle < 100; shuffle++)
        {
            ProfitShareInput[] shuffled = [.. basePartners.OrderBy(_ => random.Next())];
            Dictionary<Guid, decimal> result = ProfitShareAllocator
                .Allocate(10.00m, shuffled)
                .ToDictionary(allocation => allocation.HolderId, allocation => allocation.AllocatedAmount);

            foreach ((Guid holderId, decimal amount) in baseline)
            {
                Assert.Equal(amount, result[holderId]);
            }
        }
    }

    [Fact]
    public void TheRemainderGoesToTheLargestRemainderFirst()
    {
        var high = new ProfitShareInput { HolderId = Guid.NewGuid(), ShareBasisPoints = 3334 };
        var mid = new ProfitShareInput { HolderId = Guid.NewGuid(), ShareBasisPoints = 3333 };
        var low = new ProfitShareInput { HolderId = Guid.NewGuid(), ShareBasisPoints = 3333 };

        IReadOnlyList<ProfitShareAllocation> allocations = ProfitShareAllocator.Allocate(
            0.01m,
            [high, mid, low]);

        ProfitShareAllocation highResult = allocations.Single(allocation => allocation.HolderId == high.HolderId);
        Assert.Equal(0.01m, highResult.AllocatedAmount);
        Assert.True(highResult.RemainderUnitAwarded);
        Assert.Equal(0.00m, allocations.Single(allocation => allocation.HolderId == mid.HolderId).AllocatedAmount);
        Assert.Equal(0.00m, allocations.Single(allocation => allocation.HolderId == low.HolderId).AllocatedAmount);
    }

    [Fact]
    public void TiesAreBrokenByShareThenByHolderId()
    {
        // Equal shares mean equal remainders, so the tie falls through to holder creation order.
        // Explicit timestamps (rather than a wall-clock sleep) keep this deterministic.
        var earlier = new ProfitShareInput
        {
            HolderId = Guid.CreateVersion7(DateTimeOffset.UnixEpoch),
            ShareBasisPoints = 5000,
        };
        var later = new ProfitShareInput
        {
            HolderId = Guid.CreateVersion7(DateTimeOffset.UnixEpoch.AddMilliseconds(1)),
            ShareBasisPoints = 5000,
        };

        IReadOnlyList<ProfitShareAllocation> allocations = ProfitShareAllocator.Allocate(
            0.01m,
            [later, earlier]);

        Assert.Equal(
            0.01m,
            allocations.Single(allocation => allocation.HolderId == earlier.HolderId).AllocatedAmount);
        Assert.Equal(
            0.00m,
            allocations.Single(allocation => allocation.HolderId == later.HolderId).AllocatedAmount);
    }

    [Fact]
    public void TheMaximumAllowedAmountAllocatesWithoutOverflow()
    {
        // FinanceAmount.MaximumAmount (1e9 TRY = 1e11 minor units) sits far below the overflow
        // guard's threshold (long.MaxValue / 10_000 ~= 9.22e14), so this exercises the largest
        // input the public API can ever hand the allocator without tripping that guard.
        IReadOnlyList<ProfitShareInput> partners = ToPartners([10_000]);

        IReadOnlyList<ProfitShareAllocation> allocations = ProfitShareAllocator.Allocate(
            FinanceAmount.MaximumAmount,
            partners);

        Assert.Equal(FinanceAmount.MaximumAmount, Assert.Single(allocations).AllocatedAmount);
    }

    [Fact]
    public void ASinglePartnerTakesEverything()
    {
        IReadOnlyList<ProfitShareInput> partners = ToPartners([10_000]);

        IReadOnlyList<ProfitShareAllocation> allocations = ProfitShareAllocator.Allocate(123.45m, partners);

        Assert.Equal(123.45m, Assert.Single(allocations).AllocatedAmount);
    }

    [Fact]
    public void AnEmptyPartnerListIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ProfitShareAllocator.Allocate(100m, []));
    }

    [Fact]
    public void ADuplicateHolderIsRejected()
    {
        Guid holderId = Guid.NewGuid();
        ProfitShareInput[] partners =
        [
            new() { HolderId = holderId, ShareBasisPoints = 5000 },
            new() { HolderId = holderId, ShareBasisPoints = 5000 },
        ];

        Assert.Throws<ArgumentException>(() => ProfitShareAllocator.Allocate(100m, partners));
    }

    [Fact]
    public void AShareOutsideOneTo10000IsRejected()
    {
        ProfitShareInput[] partners = [new() { HolderId = Guid.NewGuid(), ShareBasisPoints = 0 }];

        Assert.Throws<ArgumentOutOfRangeException>(() => ProfitShareAllocator.Allocate(100m, partners));
    }

    private static IReadOnlyList<ProfitShareInput> ToPartners(int[] shares) =>
        [.. shares.Select(share => new ProfitShareInput { HolderId = Guid.NewGuid(), ShareBasisPoints = share })];
}
