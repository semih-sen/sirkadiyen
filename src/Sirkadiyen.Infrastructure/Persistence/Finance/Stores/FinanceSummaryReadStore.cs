using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Stores;

/// <summary>
/// Computes the ten period figures (ADR-093) as aggregate queries over
/// <c>finance_ledger_entries</c>, <c>finance_obligations</c> and <c>finance_settlements</c>.
/// Nothing here reads the cached <see cref="FinanceObligation.SettledAmount"/> for a historical
/// figure — Receivables/Debts are recomputed from settlements dated on or before the period end.
/// </summary>
public sealed class FinanceSummaryReadStore(SirkadiyenDbContext dbContext) : IFinanceSummaryReadStore
{
    public async Task<FinanceSummary> GetSummaryAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        DateOnly today,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        decimal carriedOver = await BalanceAsOfAsync(periodStartOn.AddDays(-1), accountId, cancellationToken);
        decimal toBeCarriedOver = await BalanceAsOfAsync(periodEndOn, accountId, cancellationToken);
        decimal currentBalance = await BalanceAsOfAsync(today, accountId, cancellationToken);

        decimal income = await SumEntriesAsync(
            FinanceTransactionKind.Income,
            periodStartOn,
            periodEndOn,
            accountId,
            cancellationToken);
        decimal expensesRaw = await SumEntriesAsync(
            FinanceTransactionKind.Expense,
            periodStartOn,
            periodEndOn,
            accountId,
            cancellationToken);
        decimal expenses = -expensesRaw;

        decimal receivables = await OutstandingAsOfAsync(
            FinanceObligationDirection.Receivable,
            periodEndOn,
            cancellationToken);
        decimal debts = await OutstandingAsOfAsync(
            FinanceObligationDirection.Payable,
            periodEndOn,
            cancellationToken);
        decimal collections = await SettledInPeriodAsync(
            FinanceObligationDirection.Receivable,
            periodStartOn,
            periodEndOn,
            cancellationToken);
        decimal payments = await SettledInPeriodAsync(
            FinanceObligationDirection.Payable,
            periodStartOn,
            periodEndOn,
            cancellationToken);

        IReadOnlyList<FinanceCategoryTotal> categoryTotals = await GetCategoryTotalsAsync(
            periodStartOn,
            periodEndOn,
            accountId,
            cancellationToken);

        return new FinanceSummary
        {
            PeriodStartOn = periodStartOn,
            PeriodEndOn = periodEndOn,
            AccountId = accountId,
            CarriedOver = carriedOver,
            Income = income,
            Expenses = expenses,
            Balance = income - expenses,
            CurrentBalance = currentBalance,
            AsOfOn = today,
            ToBeCarriedOver = toBeCarriedOver,
            Receivables = receivables,
            Collections = collections,
            Debts = debts,
            Payments = payments,
            PeriodStartsInFuture = periodStartOn > today,
            PeriodIsClosed = periodEndOn < today,
            CategoryTotals = categoryTotals,
        };
    }

    public async Task<IReadOnlyList<FinanceTrendPoint>> GetTrendAsync(
        int months,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        DateOnly windowStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        var grouped = await dbContext.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.OccurredOn >= windowStart
                && (entry.Kind == FinanceTransactionKind.Income || entry.Kind == FinanceTransactionKind.Expense))
            .GroupBy(entry => new { entry.OccurredOn.Year, entry.OccurredOn.Month, entry.Kind })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Kind,
                Sum = group.Sum(entry => entry.Amount),
            })
            .ToListAsync(cancellationToken);

        List<(int Year, int Month, FinanceTransactionKind Kind, decimal Sum)> rows =
            [.. grouped.Select(row => (row.Year, row.Month, row.Kind, row.Sum))];

        List<FinanceTrendPoint> points = [];
        for (int offset = months - 1; offset >= 0; offset--)
        {
            DateOnly monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-offset);
            decimal income = rows
                .Where(row => row.Year == monthStart.Year && row.Month == monthStart.Month
                    && row.Kind == FinanceTransactionKind.Income)
                .Sum(row => row.Sum);
            decimal expenses = -rows
                .Where(row => row.Year == monthStart.Year && row.Month == monthStart.Month
                    && row.Kind == FinanceTransactionKind.Expense)
                .Sum(row => row.Sum);

            points.Add(new FinanceTrendPoint
            {
                Year = monthStart.Year,
                Month = monthStart.Month,
                Income = income,
                Expenses = expenses,
                Net = income - expenses,
            });
        }

        return points;
    }

    private async Task<decimal> BalanceAsOfAsync(
        DateOnly asOfOn,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        IQueryable<FinanceLedgerEntry> entries = dbContext.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.OccurredOn <= asOfOn);
        if (accountId is { } id)
        {
            entries = entries.Where(entry => entry.FinanceAccountId == id);
        }

        decimal? sum = await entries.SumAsync(entry => (decimal?)entry.Amount, cancellationToken);
        return sum ?? 0m;
    }

    private async Task<decimal> SumEntriesAsync(
        FinanceTransactionKind kind,
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        IQueryable<FinanceLedgerEntry> entries = dbContext.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.Kind == kind
                && entry.OccurredOn >= periodStartOn
                && entry.OccurredOn <= periodEndOn);
        if (accountId is { } id)
        {
            entries = entries.Where(entry => entry.FinanceAccountId == id);
        }

        decimal? sum = await entries.SumAsync(entry => (decimal?)entry.Amount, cancellationToken);
        return sum ?? 0m;
    }

    private async Task<IReadOnlyList<FinanceCategoryTotal>> GetCategoryTotalsAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        IQueryable<FinanceLedgerEntry> entries = dbContext.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => (entry.Kind == FinanceTransactionKind.Income
                    || entry.Kind == FinanceTransactionKind.Expense)
                && entry.OccurredOn >= periodStartOn
                && entry.OccurredOn <= periodEndOn);
        if (accountId is { } id)
        {
            entries = entries.Where(entry => entry.FinanceAccountId == id);
        }

        Guid[] transactionIds = await entries
            .Select(entry => entry.FinanceTransactionId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var grouped = await dbContext.FinanceTransactions
            .AsNoTracking()
            .Where(transaction => transactionIds.Contains(transaction.Id) && transaction.Category != null)
            .GroupBy(transaction => new { Category = transaction.Category!.Value, transaction.Kind })
            .Select(group => new { group.Key.Category, group.Key.Kind, Sum = group.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        return [.. grouped.Select(row => new FinanceCategoryTotal
        {
            Category = row.Category,
            Kind = row.Kind,
            Total = row.Sum,
        })];
    }

    private async Task<decimal> OutstandingAsOfAsync(
        FinanceObligationDirection direction,
        DateOnly asOfOn,
        CancellationToken cancellationToken)
    {
        // A GroupBy subquery joined back with DefaultIfEmpty does not translate reliably here
        // (EF/Npgsql loses the outer-join null guard on the aggregated column), so the obligation
        // and settlement sides are fetched separately and joined in memory instead. Obligation
        // volumes are small — tens a month — so this stays cheap.
        List<FinanceObligation> obligations = await dbContext.FinanceObligations
            .AsNoTracking()
            .Where(obligation => obligation.Direction == direction
                && obligation.IssuedOn <= asOfOn
                && obligation.Status != FinanceObligationStatus.Cancelled
                && (obligation.WrittenOffOn == null || obligation.WrittenOffOn > asOfOn))
            .ToListAsync(cancellationToken);
        if (obligations.Count == 0)
        {
            return 0m;
        }

        Guid[] obligationIds = [.. obligations.Select(obligation => obligation.Id)];
        List<FinanceSettlement> settlements = await dbContext.FinanceSettlements
            .AsNoTracking()
            .Where(settlement => obligationIds.Contains(settlement.FinanceObligationId)
                && settlement.SettledOn <= asOfOn)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, decimal> settledByObligationId = settlements
            .GroupBy(settlement => settlement.FinanceObligationId)
            .ToDictionary(group => group.Key, group => group.Sum(settlement => settlement.Amount));

        return obligations.Sum(obligation => Math.Max(
            0m,
            obligation.Amount - settledByObligationId.GetValueOrDefault(obligation.Id)));
    }

    private async Task<decimal> SettledInPeriodAsync(
        FinanceObligationDirection direction,
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        CancellationToken cancellationToken)
    {
        decimal? sum = await dbContext.FinanceSettlements
            .AsNoTracking()
            .Where(settlement => settlement.Direction == direction
                && settlement.SettledOn >= periodStartOn
                && settlement.SettledOn <= periodEndOn)
            .SumAsync(settlement => (decimal?)settlement.Amount, cancellationToken);
        return sum ?? 0m;
    }
}
