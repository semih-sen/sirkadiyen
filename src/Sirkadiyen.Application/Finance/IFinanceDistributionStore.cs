using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

public enum FinanceDistributionExclusionReason
{
    NotAPartner,
    HolderInactive,
    HolderHasNoShare,
}

public sealed record FinanceDistributionExclusion
{
    public required Guid HolderId { get; init; }

    public required string HolderDisplayName { get; init; }

    public required FinanceDistributionExclusionReason Reason { get; init; }
}

public sealed record FinanceDistributionPlanShare
{
    public required Guid HolderId { get; init; }

    public required string HolderDisplayName { get; init; }

    public required int ShareBasisPoints { get; init; }

    public required long ExactShareMinorUnits { get; init; }

    public required decimal AllocatedAmount { get; init; }

    public required bool RemainderUnitAwarded { get; init; }
}

public enum FinanceDistributionPlanOutcome
{
    Ready,
    NothingToDistribute,
    NoEligiblePartners,
    SharesDoNotSumToTotal,
    SourceAccountNotFound,
    SourceAccountClosed,
    AlreadyDistributedForPeriod,
}

/// <summary>
/// The server-computed impact of a prospective distribution. Carries a fresh
/// <see cref="ConfirmationToken"/> and <see cref="PlanHash"/> when <see cref="Outcome"/> is
/// <see cref="FinanceDistributionPlanOutcome.Ready"/>, which is what makes preview binding at
/// execution rather than merely advisory (ADR-093).
/// </summary>
public sealed record FinanceDistributionPlan
{
    public required FinanceDistributionPlanOutcome Outcome { get; init; }

    public DateOnly PeriodStartOn { get; init; }

    public DateOnly PeriodEndOn { get; init; }

    public Guid? SourceAccountId { get; init; }

    public decimal DistributableAmount { get; init; }

    public required IReadOnlyList<FinanceDistributionPlanShare> Shares { get; init; }

    public required IReadOnlyList<FinanceDistributionExclusion> Exclusions { get; init; }

    public Guid? ConfirmationToken { get; init; }

    public string? PlanHash { get; init; }

    /// <summary>The distributable amount formatted invariant "0.00" — what execute expects back.</summary>
    public string? ExpectedConfirmationPhrase { get; init; }
}

public enum FinanceDistributionOutcome
{
    Executed,
    Reversed,
    ReplayedExistingExecution,
    AlreadyDistributedForPeriod,
    PlanChanged,
    ConfirmationPhraseMismatch,
    NothingToDistribute,
    SharesDoNotSumToTotal,
    NoEligiblePartners,
    SourceAccountNotFound,
    SourceAccountClosed,
    InsufficientSourceBalance,
    NotFound,
    AlreadyReversed,
}

public sealed record FinanceDistributionResult
{
    public required FinanceDistributionOutcome Outcome { get; init; }

    public Guid? DistributionId { get; init; }
}

public sealed record FinanceDistributionListItem
{
    public required Guid DistributionId { get; init; }

    public required DateOnly PeriodStartOn { get; init; }

    public required DateOnly PeriodEndOn { get; init; }

    public required Guid SourceFinanceAccountId { get; init; }

    public required decimal DistributableAmount { get; init; }

    public required FinanceDistributionStatus Status { get; init; }

    public required DateTimeOffset ExecutedAtUtc { get; init; }
}

/// <summary>
/// The six-step high-risk profit distribution pattern (design-plan §4.3): scope, server-side
/// compute, review, preview with a binding hash, strong confirmation, audit. Preview performs no
/// writes; execute recomputes the plan itself and refuses if it no longer matches.
/// </summary>
public interface IFinanceDistributionStore
{
    Task<FinanceDistributionPlan> PreviewAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid sourceAccountId,
        CancellationToken cancellationToken);

    Task<FinanceDistributionResult> ExecuteAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid sourceAccountId,
        Guid confirmationToken,
        string planHash,
        string expectedConfirmationPhrase,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceDistributionResult> ReverseAsync(
        Guid distributionId,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceDistributionListItem>> ListAsync(CancellationToken cancellationToken);

    Task<FinanceDistributionListItem?> FindAsync(Guid distributionId, CancellationToken cancellationToken);
}
