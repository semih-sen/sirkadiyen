namespace Sirkadiyen.Application.Finance;

public enum FinancePeriodSelector
{
    CurrentMonth,
    PreviousMonth,
    NextMonth,
    LastThreeMonths,
    NextThreeMonths,
    Custom,
}

public sealed record FinancePeriod
{
    public required DateOnly StartOn { get; init; }

    public required DateOnly EndOn { get; init; }
}

public enum FinancePeriodResolutionOutcome
{
    Resolved,
    CustomRangeRequiresBothDates,
    EndBeforeStart,
    RangeTooLong,
}

public sealed record FinancePeriodResolutionResult
{
    public required FinancePeriodResolutionOutcome Outcome { get; init; }

    public FinancePeriod? Period { get; init; }
}

/// <summary>
/// Resolves a period selector against Istanbul "today", using the same
/// <c>TimeZoneInfo.FindSystemTimeZoneById</c> idiom as
/// <c>ScheduleEndpoints.GetUpcomingAsync</c>. Both three-month directions are named explicitly
/// (<see cref="FinancePeriodSelector.LastThreeMonths"/> / <see cref="FinancePeriodSelector.NextThreeMonths"/>)
/// rather than picking one silently; both include the current month and extend two more in their
/// respective direction.
/// </summary>
public sealed class FinancePeriodResolver(TimeProvider timeProvider)
{
    public const string TimeZoneId = "Europe/Istanbul";

    public const int MaximumPeriodDays = 366;

    public DateOnly Today()
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
    }

    public FinancePeriodResolutionResult Resolve(
        FinancePeriodSelector selector,
        DateOnly? customStartOn,
        DateOnly? customEndOn)
    {
        DateOnly today = Today();
        FinancePeriod period;

        switch (selector)
        {
            case FinancePeriodSelector.CurrentMonth:
                period = MonthPeriod(today.Year, today.Month);
                break;

            case FinancePeriodSelector.PreviousMonth:
                DateOnly previous = today.AddMonths(-1);
                period = MonthPeriod(previous.Year, previous.Month);
                break;

            case FinancePeriodSelector.NextMonth:
                DateOnly next = today.AddMonths(1);
                period = MonthPeriod(next.Year, next.Month);
                break;

            case FinancePeriodSelector.LastThreeMonths:
                period = ThreeMonthPeriod(today, forward: false);
                break;

            case FinancePeriodSelector.NextThreeMonths:
                period = ThreeMonthPeriod(today, forward: true);
                break;

            case FinancePeriodSelector.Custom:
                if (customStartOn is null || customEndOn is null)
                {
                    return new FinancePeriodResolutionResult
                    {
                        Outcome = FinancePeriodResolutionOutcome.CustomRangeRequiresBothDates,
                    };
                }

                period = new FinancePeriod { StartOn = customStartOn.Value, EndOn = customEndOn.Value };
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(selector), selector, null);
        }

        if (period.EndOn < period.StartOn)
        {
            return new FinancePeriodResolutionResult { Outcome = FinancePeriodResolutionOutcome.EndBeforeStart };
        }

        int spanDays = period.EndOn.DayNumber - period.StartOn.DayNumber + 1;
        if (spanDays > MaximumPeriodDays)
        {
            return new FinancePeriodResolutionResult { Outcome = FinancePeriodResolutionOutcome.RangeTooLong };
        }

        return new FinancePeriodResolutionResult
        {
            Outcome = FinancePeriodResolutionOutcome.Resolved,
            Period = period,
        };
    }

    private static FinancePeriod MonthPeriod(int year, int month)
    {
        DateOnly start = new(year, month, 1);
        DateOnly end = start.AddMonths(1).AddDays(-1);
        return new FinancePeriod { StartOn = start, EndOn = end };
    }

    private static FinancePeriod ThreeMonthPeriod(DateOnly today, bool forward)
    {
        DateOnly currentMonthStart = new(today.Year, today.Month, 1);

        if (forward)
        {
            DateOnly end = currentMonthStart.AddMonths(3).AddDays(-1);
            return new FinancePeriod { StartOn = currentMonthStart, EndOn = end };
        }

        DateOnly start = currentMonthStart.AddMonths(-2);
        DateOnly monthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
        return new FinancePeriod { StartOn = start, EndOn = monthEnd };
    }
}
