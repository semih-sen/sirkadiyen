using Sirkadiyen.Application.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinancePeriodResolverTests
{
    [Fact]
    public void CurrentMonthCoversTheWholeMonthEvenWhenTodayIsTheThirtyFirst()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 1, 31));

        FinancePeriodResolutionResult result = resolver.Resolve(
            FinancePeriodSelector.CurrentMonth,
            null,
            null);

        Assert.Equal(FinancePeriodResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Period!.StartOn);
        Assert.Equal(new DateOnly(2026, 1, 31), result.Period.EndOn);
    }

    [Fact]
    public void NextMonthFromTheThirtyFirstOfJanuaryIsAllOfFebruary()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 1, 31));

        FinancePeriodResolutionResult result = resolver.Resolve(FinancePeriodSelector.NextMonth, null, null);

        Assert.Equal(new DateOnly(2026, 2, 1), result.Period!.StartOn);
        Assert.Equal(new DateOnly(2026, 2, 28), result.Period.EndOn);
    }

    [Fact]
    public void NextMonthFromDecemberRollsIntoTheFollowingYear()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 12, 15));

        FinancePeriodResolutionResult result = resolver.Resolve(FinancePeriodSelector.NextMonth, null, null);

        Assert.Equal(new DateOnly(2027, 1, 1), result.Period!.StartOn);
        Assert.Equal(new DateOnly(2027, 1, 31), result.Period.EndOn);
    }

    [Fact]
    public void PreviousMonthFromJanuaryRollsIntoThePriorYear()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 1, 10));

        FinancePeriodResolutionResult result = resolver.Resolve(FinancePeriodSelector.PreviousMonth, null, null);

        Assert.Equal(new DateOnly(2025, 12, 1), result.Period!.StartOn);
        Assert.Equal(new DateOnly(2025, 12, 31), result.Period.EndOn);
    }

    [Fact]
    public void LastThreeMonthsSpansAYearBoundaryAndIncludesTheCurrentMonth()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 2, 10));

        FinancePeriodResolutionResult result = resolver.Resolve(
            FinancePeriodSelector.LastThreeMonths,
            null,
            null);

        Assert.Equal(new DateOnly(2025, 12, 1), result.Period!.StartOn);
        Assert.Equal(new DateOnly(2026, 2, 28), result.Period.EndOn);
    }

    [Fact]
    public void NextThreeMonthsSpansAYearBoundaryAndIncludesTheCurrentMonth()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 11, 10));

        FinancePeriodResolutionResult result = resolver.Resolve(
            FinancePeriodSelector.NextThreeMonths,
            null,
            null);

        Assert.Equal(new DateOnly(2026, 11, 1), result.Period!.StartOn);
        Assert.Equal(new DateOnly(2027, 1, 31), result.Period.EndOn);
    }

    [Fact]
    public void CustomRequiresBothDates()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 1, 1));

        FinancePeriodResolutionResult missingEnd = resolver.Resolve(
            FinancePeriodSelector.Custom,
            new DateOnly(2026, 1, 1),
            null);
        FinancePeriodResolutionResult missingStart = resolver.Resolve(
            FinancePeriodSelector.Custom,
            null,
            new DateOnly(2026, 1, 31));

        Assert.Equal(FinancePeriodResolutionOutcome.CustomRangeRequiresBothDates, missingEnd.Outcome);
        Assert.Equal(FinancePeriodResolutionOutcome.CustomRangeRequiresBothDates, missingStart.Outcome);
    }

    [Fact]
    public void CustomRejectsAnEndDateBeforeTheStartDate()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 1, 1));

        FinancePeriodResolutionResult result = resolver.Resolve(
            FinancePeriodSelector.Custom,
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 1, 1));

        Assert.Equal(FinancePeriodResolutionOutcome.EndBeforeStart, result.Outcome);
    }

    [Fact]
    public void CustomRejectsASpanLongerThanTheMaximum()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 1, 1));
        DateOnly start = new(2026, 1, 1);
        DateOnly end = start.AddDays(FinancePeriodResolver.MaximumPeriodDays);

        FinancePeriodResolutionResult result = resolver.Resolve(FinancePeriodSelector.Custom, start, end);

        Assert.Equal(FinancePeriodResolutionOutcome.RangeTooLong, result.Outcome);
    }

    [Fact]
    public void CustomAcceptsASpanExactlyAtTheMaximum()
    {
        var resolver = new FinancePeriodResolver(AtIstanbul(2026, 1, 1));
        DateOnly start = new(2026, 1, 1);
        DateOnly end = start.AddDays(FinancePeriodResolver.MaximumPeriodDays - 1);

        FinancePeriodResolutionResult result = resolver.Resolve(FinancePeriodSelector.Custom, start, end);

        Assert.Equal(FinancePeriodResolutionOutcome.Resolved, result.Outcome);
    }

    [Fact]
    public void LateNightUtcResolvesToTheNextIstanbulDay()
    {
        // Istanbul is UTC+3 year-round; 23:30 UTC on the 1st is already 02:30 on the 2nd there.
        var fixedTime = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero));
        var resolver = new FinancePeriodResolver(fixedTime);

        DateOnly today = resolver.Today();

        Assert.Equal(new DateOnly(2026, 1, 2), today);
    }

    private static FixedTimeProvider AtIstanbul(int year, int month, int day) =>
        new(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.FromHours(3)));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
