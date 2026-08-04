namespace Sirkadiyen.Application.Finance;

public enum FinanceSummaryOutcome
{
    Resolved,
    CustomRangeRequiresBothDates,
    EndBeforeStart,
    RangeTooLong,
}

public sealed record FinanceSummaryResult
{
    public required FinanceSummaryOutcome Outcome { get; init; }

    public FinanceSummary? Summary { get; init; }
}

public enum FinanceTrendOutcome
{
    Resolved,
    InvalidMonths,
}

public sealed record FinanceTrendResult
{
    public required FinanceTrendOutcome Outcome { get; init; }

    public IReadOnlyList<FinanceTrendPoint>? Points { get; init; }
}

/// <summary>Resolves a period selector, then delegates the aggregate query to the read store.</summary>
public sealed class FinanceSummaryService(IFinanceSummaryReadStore readStore, FinancePeriodResolver periodResolver)
{
    private const int MaximumTrendMonths = 36;

    public async Task<FinanceSummaryResult> GetSummaryAsync(
        FinancePeriodSelector selector,
        DateOnly? customStartOn,
        DateOnly? customEndOn,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        FinancePeriodResolutionResult resolution = periodResolver.Resolve(selector, customStartOn, customEndOn);
        if (resolution.Outcome != FinancePeriodResolutionOutcome.Resolved || resolution.Period is not { } period)
        {
            return new FinanceSummaryResult
            {
                Outcome = resolution.Outcome switch
                {
                    FinancePeriodResolutionOutcome.CustomRangeRequiresBothDates =>
                        FinanceSummaryOutcome.CustomRangeRequiresBothDates,
                    FinancePeriodResolutionOutcome.EndBeforeStart => FinanceSummaryOutcome.EndBeforeStart,
                    FinancePeriodResolutionOutcome.RangeTooLong => FinanceSummaryOutcome.RangeTooLong,
                    _ => throw new InvalidOperationException("Unresolved period without a mapped outcome."),
                },
            };
        }

        FinanceSummary summary = await readStore.GetSummaryAsync(
            period.StartOn,
            period.EndOn,
            periodResolver.Today(),
            accountId,
            cancellationToken);
        return new FinanceSummaryResult { Outcome = FinanceSummaryOutcome.Resolved, Summary = summary };
    }

    public async Task<FinanceTrendResult> GetTrendAsync(int months, CancellationToken cancellationToken)
    {
        if (months is < 1 or > MaximumTrendMonths)
        {
            return new FinanceTrendResult { Outcome = FinanceTrendOutcome.InvalidMonths };
        }

        IReadOnlyList<FinanceTrendPoint> points = await readStore.GetTrendAsync(
            months,
            periodResolver.Today(),
            cancellationToken);
        return new FinanceTrendResult { Outcome = FinanceTrendOutcome.Resolved, Points = points };
    }
}
