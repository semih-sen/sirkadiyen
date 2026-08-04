using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

public sealed record FinanceCategoryTotal
{
    public required FinanceCategory Category { get; init; }

    public required FinanceTransactionKind Kind { get; init; }

    public required decimal Total { get; init; }
}

/// <summary>The ten period figures described in ADR-093, plus the category breakdown for the UI.</summary>
public sealed record FinanceSummary
{
    public required DateOnly PeriodStartOn { get; init; }

    public required DateOnly PeriodEndOn { get; init; }

    public Guid? AccountId { get; init; }

    /// <summary>Devreden: balance strictly before the period.</summary>
    public required decimal CarriedOver { get; init; }

    /// <summary>Gelir.</summary>
    public required decimal Income { get; init; }

    /// <summary>Gider, reported positive.</summary>
    public required decimal Expenses { get; init; }

    /// <summary>Bakiye: Income minus Expenses, computed in C#, never re-queried.</summary>
    public required decimal Balance { get; init; }

    /// <summary>Nakit: the real balance as of today, never clamped to the period.</summary>
    public required decimal CurrentBalance { get; init; }

    public required DateOnly AsOfOn { get; init; }

    /// <summary>Devredecek: balance at period end.</summary>
    public required decimal ToBeCarriedOver { get; init; }

    /// <summary>Alacak, measured as of the period end — never the cached obligation field.</summary>
    public required decimal Receivables { get; init; }

    /// <summary>Tahsilat.</summary>
    public required decimal Collections { get; init; }

    /// <summary>Borç, measured as of the period end.</summary>
    public required decimal Debts { get; init; }

    /// <summary>Ödeme.</summary>
    public required decimal Payments { get; init; }

    public required bool PeriodStartsInFuture { get; init; }

    public required bool PeriodIsClosed { get; init; }

    public required IReadOnlyList<FinanceCategoryTotal> CategoryTotals { get; init; }
}

public sealed record FinanceTrendPoint
{
    public required int Year { get; init; }

    public required int Month { get; init; }

    public required decimal Income { get; init; }

    public required decimal Expenses { get; init; }

    public required decimal Net { get; init; }
}

/// <summary>
/// Read-only period reporting. Receivables/Debts/Collections/Payments are not scoped to a single
/// account: an obligation is not tied to a cash box until it is settled (ADR-093).
/// </summary>
public interface IFinanceSummaryReadStore
{
    Task<FinanceSummary> GetSummaryAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        DateOnly today,
        Guid? accountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceTrendPoint>> GetTrendAsync(
        int months,
        DateOnly today,
        CancellationToken cancellationToken);
}
