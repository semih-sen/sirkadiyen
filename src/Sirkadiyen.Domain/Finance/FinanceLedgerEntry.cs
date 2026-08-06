namespace Sirkadiyen.Domain.Finance;

/// <summary>
/// One signed posting against one account, produced by a <see cref="FinanceTransaction"/>. Account
/// balance is <c>SUM(Amount)</c> over these rows — never a stored column — which is what makes
/// editing and deleting a transaction safe (ADR-092 §2).
/// </summary>
public sealed class FinanceLedgerEntry
{
    private FinanceLedgerEntry()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid FinanceTransactionId { get; private init; }

    public Guid FinanceAccountId { get; private init; }

    /// <summary>Denormalized from the owning transaction so the balance aggregate is join-free.</summary>
    public FinanceTransactionKind Kind { get; private init; }

    public FinanceLedgerLeg Leg { get; private init; }

    public decimal Amount { get; private init; }

    /// <summary>Denormalized from the owning transaction so period aggregates are join-free.</summary>
    public DateOnly OccurredOn { get; private init; }

    internal static FinanceLedgerEntry Create(
        Guid financeTransactionId,
        Guid financeAccountId,
        FinanceTransactionKind kind,
        FinanceLedgerLeg leg,
        decimal amount,
        DateOnly occurredOn)
    {
        if (financeTransactionId == Guid.Empty)
        {
            throw new ArgumentException("A transaction is required.", nameof(financeTransactionId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("An account is required.", nameof(financeAccountId));
        }

        return new FinanceLedgerEntry
        {
            Id = Guid.CreateVersion7(),
            FinanceTransactionId = financeTransactionId,
            FinanceAccountId = financeAccountId,
            Kind = kind,
            Leg = leg,
            Amount = FinanceAmount.RequireNonZero(amount, nameof(amount)),
            OccurredOn = occurredOn,
        };
    }
}
