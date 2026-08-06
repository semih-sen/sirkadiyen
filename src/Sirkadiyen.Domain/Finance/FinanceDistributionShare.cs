namespace Sirkadiyen.Domain.Finance;

/// <summary>
/// One partner's payout within a <see cref="FinanceDistribution"/>, produced by the largest-remainder
/// allocator. <see cref="ExactShareMinorUnits"/> is the pre-rounding numerator, kept so the
/// allocation is auditable independent of the rounded <see cref="AllocatedAmount"/>.
/// </summary>
public sealed class FinanceDistributionShare
{
    private FinanceDistributionShare()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid FinanceDistributionId { get; private init; }

    public Guid FinanceAccountHolderId { get; private init; }

    public int ShareBasisPoints { get; private init; }

    public long ExactShareMinorUnits { get; private init; }

    public decimal AllocatedAmount { get; private init; }

    public bool RemainderUnitAwarded { get; private init; }

    public Guid FinanceTransactionId { get; private init; }

    public static FinanceDistributionShare Create(
        Guid financeDistributionId,
        Guid financeAccountHolderId,
        int shareBasisPoints,
        long exactShareMinorUnits,
        decimal allocatedAmount,
        bool remainderUnitAwarded,
        Guid financeTransactionId)
    {
        if (financeDistributionId == Guid.Empty)
        {
            throw new ArgumentException("A distribution is required.", nameof(financeDistributionId));
        }

        if (financeAccountHolderId == Guid.Empty)
        {
            throw new ArgumentException("A holder is required.", nameof(financeAccountHolderId));
        }

        if (financeTransactionId == Guid.Empty)
        {
            throw new ArgumentException("A payout transaction is required.", nameof(financeTransactionId));
        }

        if (shareBasisPoints is < 1 or > FinanceAccountHolder.MaximumShareBasisPoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shareBasisPoints),
                shareBasisPoints,
                $"'{nameof(shareBasisPoints)}' must be between 1 and {FinanceAccountHolder.MaximumShareBasisPoints}.");
        }

        if (exactShareMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exactShareMinorUnits),
                exactShareMinorUnits,
                "The pre-rounding numerator cannot be negative.");
        }

        return new FinanceDistributionShare
        {
            Id = Guid.CreateVersion7(),
            FinanceDistributionId = financeDistributionId,
            FinanceAccountHolderId = financeAccountHolderId,
            ShareBasisPoints = shareBasisPoints,
            ExactShareMinorUnits = exactShareMinorUnits,
            AllocatedAmount = FinanceAmount.RequirePositive(allocatedAmount, nameof(allocatedAmount)),
            RemainderUnitAwarded = remainderUnitAwarded,
            FinanceTransactionId = financeTransactionId,
        };
    }
}
