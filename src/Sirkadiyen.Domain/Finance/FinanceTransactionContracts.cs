namespace Sirkadiyen.Domain.Finance;

/// <summary>A transaction together with the ledger entries it produced or now produces.</summary>
public sealed record FinancePosting(
    FinanceTransaction Transaction,
    IReadOnlyList<FinanceLedgerEntry> Entries);

/// <summary>The fields of a transaction that <see cref="FinanceTransaction.Rewrite"/> may change.</summary>
public sealed record FinanceTransactionEdit
{
    public required FinanceTransactionKind Kind { get; init; }

    public FinanceCategory? Category { get; init; }

    public required decimal Amount { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string Description { get; init; }

    public string? Reference { get; init; }

    public string? CounterpartyName { get; init; }

    /// <summary>The account for Income/Expense; the source (From) account for a Transfer.</summary>
    public required Guid AccountId { get; init; }

    /// <summary>The destination (To) account. Required for, and only meaningful for, a Transfer.</summary>
    public Guid? ToAccountId { get; init; }
}
