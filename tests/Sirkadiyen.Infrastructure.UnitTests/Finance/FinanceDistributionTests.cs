using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinanceDistributionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void ExecuteRejectsAPeriodEndingBeforeItStarts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceDistribution.Execute(
            Today,
            Today.AddDays(-1),
            Guid.NewGuid(),
            100m,
            Guid.NewGuid(),
            new string('a', 64),
            "Q1 distribution.",
            Actor,
            "admin@example.com",
            Now));
    }

    [Fact]
    public void ExecuteRejectsAPlanHashOfTheWrongLength()
    {
        Assert.Throws<ArgumentException>(() => FinanceDistribution.Execute(
            Today,
            Today,
            Guid.NewGuid(),
            100m,
            Guid.NewGuid(),
            "too-short",
            "Q1 distribution.",
            Actor,
            "admin@example.com",
            Now));
    }

    [Fact]
    public void ExecuteCreatesAnExecutedDistribution()
    {
        FinanceDistribution distribution = FinanceDistribution.Execute(
            Today,
            Today,
            Guid.NewGuid(),
            100m,
            Guid.NewGuid(),
            new string('a', 64),
            "Q1 distribution.",
            Actor,
            "admin@example.com",
            Now);

        Assert.Equal(FinanceDistributionStatus.Executed, distribution.Status);
        Assert.Null(distribution.ReversedAtUtc);
    }

    [Fact]
    public void ReverseRequiresAReasonAndFlipsStatus()
    {
        FinanceDistribution distribution = FinanceDistribution.Execute(
            Today,
            Today,
            Guid.NewGuid(),
            100m,
            Guid.NewGuid(),
            new string('a', 64),
            "Q1 distribution.",
            Actor,
            "admin@example.com",
            Now);

        distribution.Reverse(Actor, "admin@example.com", "Miscalculated profit.", Now.AddDays(1));

        Assert.Equal(FinanceDistributionStatus.Reversed, distribution.Status);
        Assert.Equal("Miscalculated profit.", distribution.ReversalReason);
    }

    [Fact]
    public void ReversingAnAlreadyReversedDistributionIsRejected()
    {
        FinanceDistribution distribution = FinanceDistribution.Execute(
            Today,
            Today,
            Guid.NewGuid(),
            100m,
            Guid.NewGuid(),
            new string('a', 64),
            "Q1 distribution.",
            Actor,
            "admin@example.com",
            Now);
        distribution.Reverse(Actor, "admin@example.com", "Miscalculated profit.", Now.AddDays(1));

        Assert.Throws<InvalidOperationException>(
            () => distribution.Reverse(Actor, "admin@example.com", "Again.", Now.AddDays(2)));
    }

    [Fact]
    public void DistributionShareRejectsAShareOutsideOneTo10000()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceDistributionShare.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            0,
            10m,
            false,
            Guid.NewGuid()));
    }
}
