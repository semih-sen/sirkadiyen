namespace Sirkadiyen.Domain.Finance;

public enum FinanceDistributionStatus
{
    Executed,
    Reversed,
}

/// <summary>
/// A profit distribution execution: non-repeatable per period and idempotent per confirmation
/// token, both enforced by a unique index rather than application logic alone (ADR-092 §8). The
/// allocation plan and its <see cref="PlanHash"/> are computed by
/// <c>Sirkadiyen.Application.Finance.ProfitShareAllocator</c>; this type only records the outcome.
/// </summary>
public sealed class FinanceDistribution
{
    public const int MaximumReasonLength = 2000;

    public const int MaximumActorEmailLength = 320;

    public const int PlanHashLength = 64;

    private FinanceDistribution()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public DateOnly PeriodStartOn { get; private init; }

    public DateOnly PeriodEndOn { get; private init; }

    public Guid SourceFinanceAccountId { get; private init; }

    public decimal DistributableAmount { get; private init; }

    public FinanceDistributionStatus Status { get; private set; }

    public Guid ConfirmationToken { get; private init; }

    /// <summary>SHA-256 hex of the canonical plan, making the preview step binding at execution.</summary>
    public string PlanHash { get; private init; } = string.Empty;

    public string Reason { get; private init; } = string.Empty;

    public Guid ExecutedByUserId { get; private init; }

    public string ExecutedByEmail { get; private init; } = string.Empty;

    public DateTimeOffset ExecutedAtUtc { get; private init; }

    public Guid? ReversedByUserId { get; private set; }

    public string? ReversedByEmail { get; private set; }

    public string? ReversalReason { get; private set; }

    public DateTimeOffset? ReversedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public static FinanceDistribution Execute(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid sourceFinanceAccountId,
        decimal distributableAmount,
        Guid confirmationToken,
        string planHash,
        string reason,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc)
    {
        if (periodEndOn < periodStartOn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodEndOn),
                periodEndOn,
                "A distribution period cannot end before it starts.");
        }

        if (sourceFinanceAccountId == Guid.Empty)
        {
            throw new ArgumentException("A source account is required.", nameof(sourceFinanceAccountId));
        }

        if (confirmationToken == Guid.Empty)
        {
            throw new ArgumentException("A confirmation token is required.", nameof(confirmationToken));
        }

        distributableAmount = FinanceAmount.RequirePositive(
            distributableAmount,
            nameof(distributableAmount));

        ArgumentException.ThrowIfNullOrWhiteSpace(planHash, nameof(planHash));
        if (planHash.Length != PlanHashLength)
        {
            throw new ArgumentException(
                $"A plan hash must contain exactly {PlanHashLength} characters.",
                nameof(planHash));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }

        reason = RequiredBounded(reason, MaximumReasonLength, nameof(reason));
        actorEmail = RequiredBounded(actorEmail, MaximumActorEmailLength, nameof(actorEmail));

        return new FinanceDistribution
        {
            Id = Guid.CreateVersion7(),
            PeriodStartOn = periodStartOn,
            PeriodEndOn = periodEndOn,
            SourceFinanceAccountId = sourceFinanceAccountId,
            DistributableAmount = distributableAmount,
            Status = FinanceDistributionStatus.Executed,
            ConfirmationToken = confirmationToken,
            PlanHash = planHash,
            Reason = reason,
            ExecutedByUserId = actorUserId,
            ExecutedByEmail = actorEmail,
            ExecutedAtUtc = nowUtc,
        };
    }

    public void Reverse(
        Guid actorUserId,
        string actorEmail,
        string reversalReason,
        DateTimeOffset reversedAtUtc)
    {
        if (Status == FinanceDistributionStatus.Reversed)
        {
            throw new InvalidOperationException("The distribution is already reversed.");
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }

        Status = FinanceDistributionStatus.Reversed;
        ReversedByUserId = actorUserId;
        ReversedByEmail = RequiredBounded(actorEmail, MaximumActorEmailLength, nameof(actorEmail));
        ReversalReason = RequiredBounded(reversalReason, MaximumReasonLength, nameof(reversalReason));
        ReversedAtUtc = reversedAtUtc;
    }

    private static string RequiredBounded(string? value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}

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
