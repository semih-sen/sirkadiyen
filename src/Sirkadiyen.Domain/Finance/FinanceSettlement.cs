namespace Sirkadiyen.Domain.Finance;

/// <summary>Links an obligation to the ordinary cash transaction that settled part of it.</summary>
public sealed class FinanceSettlement
{
    private FinanceSettlement()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid FinanceObligationId { get; private init; }

    public Guid FinanceTransactionId { get; private init; }

    /// <summary>Denormalized from the obligation.</summary>
    public FinanceObligationDirection Direction { get; private init; }

    public decimal Amount { get; private init; }

    /// <summary>Copied from the settling cash transaction's <c>OccurredOn</c>.</summary>
    public DateOnly SettledOn { get; private init; }

    public DateTimeOffset RecordedAtUtc { get; private init; }

    public static FinanceSettlement Create(
        Guid financeObligationId,
        Guid financeTransactionId,
        FinanceObligationDirection direction,
        decimal amount,
        DateOnly settledOn,
        DateTimeOffset recordedAtUtc)
    {
        if (financeObligationId == Guid.Empty)
        {
            throw new ArgumentException("An obligation is required.", nameof(financeObligationId));
        }

        if (financeTransactionId == Guid.Empty)
        {
            throw new ArgumentException("A transaction is required.", nameof(financeTransactionId));
        }

        return new FinanceSettlement
        {
            Id = Guid.CreateVersion7(),
            FinanceObligationId = financeObligationId,
            FinanceTransactionId = financeTransactionId,
            Direction = direction,
            Amount = FinanceAmount.RequirePositive(amount, nameof(amount)),
            SettledOn = settledOn,
            RecordedAtUtc = recordedAtUtc,
        };
    }
}
