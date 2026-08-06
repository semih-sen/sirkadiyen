using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Transactional PostgreSQL store for profit distributions: the six-step preview/execute/reverse
/// pattern from design-plan §4.3 (ADR-093). Preview performs no writes; execute recomputes the plan
/// itself under a lock on the source account and refuses if it no longer matches the client's hash.
/// </summary>
public sealed class FinanceDistributionStore(
    SirkadiyenDbContext dbContext,
    IFinanceSummaryReadStore summaryReadStore) : IFinanceDistributionStore
{
    private const string SubjectType = "FinanceDistribution";

    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();

    public async Task<FinanceDistributionPlan> PreviewAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid sourceAccountId,
        CancellationToken cancellationToken)
    {
        FinanceDistributionPlan plan = await BuildPlanAsync(
            periodStartOn,
            periodEndOn,
            sourceAccountId,
            cancellationToken);

        if (plan.Outcome != FinanceDistributionPlanOutcome.Ready)
        {
            return plan;
        }

        Guid confirmationToken = Guid.CreateVersion7();
        string planHash = ComputeHash(plan);
        return plan with
        {
            ConfirmationToken = confirmationToken,
            PlanHash = planHash,
            ExpectedConfirmationPhrase = FormatAmount(plan.DistributableAmount),
        };
    }

    public Task<FinanceDistributionResult> ExecuteAsync(
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
        CancellationToken cancellationToken)
    {
        try
        {
            return RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction dbTransaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                FinanceDistribution? existing = await dbContext.FinanceDistributions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(d => d.ConfirmationToken == confirmationToken, cancellationToken);
                if (existing is not null)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    return new FinanceDistributionResult
                    {
                        Outcome = FinanceDistributionOutcome.ReplayedExistingExecution,
                        DistributionId = existing.Id,
                    };
                }

                // Lock the source account before recomputing so the recomputed plan reflects a
                // state no concurrent transfer or edit can change out from under this execution.
                FinanceAccount? source = await dbContext.FinanceAccounts
                    .FromSql($"""
                        SELECT *, xmin FROM sirkadiyen.finance_accounts
                        WHERE "Id" = {sourceAccountId}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(cancellationToken);
                if (source is null)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    return new FinanceDistributionResult
                    {
                        Outcome = FinanceDistributionOutcome.SourceAccountNotFound,
                    };
                }

                if (source.Status == FinanceAccountStatus.Closed)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    return new FinanceDistributionResult
                    {
                        Outcome = FinanceDistributionOutcome.SourceAccountClosed,
                    };
                }

                FinanceDistributionPlan plan = await BuildPlanAsync(
                    periodStartOn,
                    periodEndOn,
                    sourceAccountId,
                    cancellationToken);
                if (plan.Outcome != FinanceDistributionPlanOutcome.Ready)
                {
                    // The initial pre-lock check for this exact token can race a concurrent
                    // execution that has not committed yet: it saw no matching row and proceeded,
                    // then waited on the account lock. Re-check by token now, under the lock, before
                    // reporting AlreadyDistributedForPeriod — the row that made it stale may be this
                    // same token's own already-committed execution, which is a replay, not a conflict.
                    if (plan.Outcome == FinanceDistributionPlanOutcome.AlreadyDistributedForPeriod)
                    {
                        FinanceDistribution? byToken = await dbContext.FinanceDistributions
                            .AsNoTracking()
                            .SingleOrDefaultAsync(d => d.ConfirmationToken == confirmationToken, cancellationToken);
                        if (byToken is not null)
                        {
                            await dbTransaction.CommitAsync(cancellationToken);
                            return new FinanceDistributionResult
                            {
                                Outcome = FinanceDistributionOutcome.ReplayedExistingExecution,
                                DistributionId = byToken.Id,
                            };
                        }
                    }

                    await dbTransaction.CommitAsync(cancellationToken);
                    return new FinanceDistributionResult { Outcome = MapPlanOutcome(plan.Outcome) };
                }

                if (ComputeHash(plan) != planHash)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    return new FinanceDistributionResult { Outcome = FinanceDistributionOutcome.PlanChanged };
                }

                if (FormatAmount(plan.DistributableAmount) != expectedConfirmationPhrase)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    return new FinanceDistributionResult
                    {
                        Outcome = FinanceDistributionOutcome.ConfirmationPhraseMismatch,
                    };
                }

                decimal balance = await dbContext.FinanceLedgerEntries
                    .Where(entry => entry.FinanceAccountId == sourceAccountId)
                    .SumAsync(entry => (decimal?)entry.Amount, cancellationToken) ?? 0m;
                if (balance < plan.DistributableAmount)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    return new FinanceDistributionResult
                    {
                        Outcome = FinanceDistributionOutcome.InsufficientSourceBalance,
                    };
                }

                FinanceDistribution distribution = FinanceDistribution.Execute(
                    periodStartOn,
                    periodEndOn,
                    sourceAccountId,
                    plan.DistributableAmount,
                    confirmationToken,
                    planHash,
                    reason,
                    actorUserId,
                    actorEmail,
                    nowUtc);
                dbContext.FinanceDistributions.Add(distribution);

                DateOnly payoutOn = DateOnly.FromDateTime(nowUtc.UtcDateTime);
                foreach (FinanceDistributionPlanShare planShare in plan.Shares)
                {
                    FinancePosting posting = FinanceTransaction.RecordDistributionPayout(
                        sourceAccountId,
                        distribution.Id,
                        planShare.AllocatedAmount,
                        payoutOn,
                        $"Profit distribution: {planShare.HolderDisplayName}",
                        planShare.HolderDisplayName,
                        actorUserId,
                        actorEmail,
                        nowUtc);
                    dbContext.FinanceTransactions.Add(posting.Transaction);
                    dbContext.FinanceLedgerEntries.AddRange(posting.Entries);

                    FinanceDistributionShare share = FinanceDistributionShare.Create(
                        distribution.Id,
                        planShare.HolderId,
                        planShare.ShareBasisPoints,
                        planShare.ExactShareMinorUnits,
                        planShare.AllocatedAmount,
                        planShare.RemainderUnitAwarded,
                        posting.Transaction.Id);
                    dbContext.FinanceDistributionShares.Add(share);

                    FinanceTransactionSnapshot after = FinanceSnapshotSerializer.Capture(
                        posting.Transaction,
                        posting.Entries);
                    dbContext.FinanceAudits.Add(FinanceAudit.Create(
                        FinanceAuditAction.TransactionCreated,
                        "FinanceTransaction",
                        posting.Transaction.Id,
                        actorUserId,
                        actorEmail,
                        nowUtc,
                        correlationId,
                        reason: null,
                        beforeState: null,
                        FinanceSnapshotSerializer.Serialize(after),
                        changedFields: null,
                        FinanceSnapshotSerializer.AmountDelta(null, after),
                        posting.Transaction.RevisionNumber));
                }

                dbContext.FinanceAudits.Add(FinanceAudit.Create(
                    FinanceAuditAction.DistributionExecuted,
                    SubjectType,
                    distribution.Id,
                    actorUserId,
                    actorEmail,
                    nowUtc,
                    correlationId,
                    reason,
                    beforeState: null,
                    Snapshot(distribution),
                    changedFields: null,
                    amountDelta: -plan.DistributableAmount,
                    revisionNumber: 1));

                await dbContext.SaveChangesAsync(cancellationToken);
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceDistributionResult
                {
                    Outcome = FinanceDistributionOutcome.Executed,
                    DistributionId = distribution.Id,
                };
            });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ReplayOrConflictAsync(confirmationToken, periodStartOn, periodEndOn, cancellationToken);
        }
    }

    public Task<FinanceDistributionResult> ReverseAsync(
        Guid distributionId,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction dbTransaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceDistribution? distribution = await dbContext.FinanceDistributions
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_distributions
                    WHERE "Id" = {distributionId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (distribution is null)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceDistributionResult { Outcome = FinanceDistributionOutcome.NotFound };
            }

            try
            {
                distribution.Reverse(actorUserId, actorEmail, reason, nowUtc);
            }
            catch (InvalidOperationException)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceDistributionResult
                {
                    Outcome = FinanceDistributionOutcome.AlreadyReversed,
                    DistributionId = distributionId,
                };
            }

            dbContext.FinanceAudits.Add(FinanceAudit.Create(
                FinanceAuditAction.DistributionReversed,
                SubjectType,
                distributionId,
                actorUserId,
                actorEmail,
                nowUtc,
                correlationId,
                reason,
                beforeState: null,
                Snapshot(distribution),
                changedFields: ["Status"],
                amountDelta: 0m,
                revisionNumber: 1));

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new FinanceDistributionResult
            {
                Outcome = FinanceDistributionOutcome.Reversed,
                DistributionId = distributionId,
            };
        });

    public async Task<IReadOnlyList<FinanceDistributionListItem>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.FinanceDistributions
            .AsNoTracking()
            .OrderByDescending(distribution => distribution.ExecutedAtUtc)
            .Select(distribution => Project(distribution))
            .ToListAsync(cancellationToken);

    public async Task<FinanceDistributionListItem?> FindAsync(
        Guid distributionId,
        CancellationToken cancellationToken) =>
        await dbContext.FinanceDistributions
            .AsNoTracking()
            .Where(distribution => distribution.Id == distributionId)
            .Select(distribution => Project(distribution))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<FinanceDistributionPlan> BuildPlanAsync(
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        Guid sourceAccountId,
        CancellationToken cancellationToken)
    {
        bool alreadyDistributed = await dbContext.FinanceDistributions
            .AsNoTracking()
            .AnyAsync(
                distribution => distribution.PeriodStartOn == periodStartOn
                    && distribution.PeriodEndOn == periodEndOn
                    && distribution.Status == FinanceDistributionStatus.Executed,
                cancellationToken);
        if (alreadyDistributed)
        {
            return EmptyPlan(FinanceDistributionPlanOutcome.AlreadyDistributedForPeriod);
        }

        FinanceAccount? source = await dbContext.FinanceAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == sourceAccountId, cancellationToken);
        if (source is null)
        {
            return EmptyPlan(FinanceDistributionPlanOutcome.SourceAccountNotFound);
        }

        if (source.Status == FinanceAccountStatus.Closed)
        {
            return EmptyPlan(FinanceDistributionPlanOutcome.SourceAccountClosed);
        }

        FinanceSummary summary = await summaryReadStore.GetSummaryAsync(
            periodStartOn,
            periodEndOn,
            periodEndOn,
            accountId: null,
            cancellationToken);
        decimal distributable = summary.Balance;
        if (distributable <= 0)
        {
            return EmptyPlan(FinanceDistributionPlanOutcome.NothingToDistribute);
        }

        List<FinanceAccountHolder> holders = await dbContext.FinanceAccountHolders
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<FinanceAccountHolder> eligible = [.. holders.Where(holder => holder.IsEligiblePartner)];
        List<FinanceDistributionExclusion> exclusions =
        [
            .. holders
                .Where(holder => !holder.IsEligiblePartner)
                .Select(holder => new FinanceDistributionExclusion
                {
                    HolderId = holder.Id,
                    HolderDisplayName = holder.DisplayName,
                    Reason = holder.Status == FinanceAccountHolderStatus.Inactive
                        ? FinanceDistributionExclusionReason.HolderInactive
                        : FinanceDistributionExclusionReason.HolderHasNoShare,
                }),
        ];

        if (eligible.Count == 0)
        {
            return EmptyPlan(FinanceDistributionPlanOutcome.NoEligiblePartners, exclusions);
        }

        int shareSum = eligible.Sum(holder => holder.ShareBasisPoints);
        if (shareSum != FinanceAccountHolder.MaximumShareBasisPoints)
        {
            return EmptyPlan(FinanceDistributionPlanOutcome.SharesDoNotSumToTotal, exclusions);
        }

        IReadOnlyList<ProfitShareAllocation> allocations = ProfitShareAllocator.Allocate(
            distributable,
            [.. eligible.Select(holder => new ProfitShareInput
            {
                HolderId = holder.Id,
                ShareBasisPoints = holder.ShareBasisPoints,
            })]);

        Dictionary<Guid, string> holderNames = eligible.ToDictionary(
            holder => holder.Id,
            holder => holder.DisplayName);

        List<FinanceDistributionPlanShare> shares =
        [
            .. allocations
                .OrderBy(allocation => allocation.HolderId)
                .Select(allocation => new FinanceDistributionPlanShare
                {
                    HolderId = allocation.HolderId,
                    HolderDisplayName = holderNames[allocation.HolderId],
                    ShareBasisPoints = allocation.ShareBasisPoints,
                    ExactShareMinorUnits = allocation.ExactShareMinorUnits,
                    AllocatedAmount = allocation.AllocatedAmount,
                    RemainderUnitAwarded = allocation.RemainderUnitAwarded,
                }),
        ];

        return new FinanceDistributionPlan
        {
            Outcome = FinanceDistributionPlanOutcome.Ready,
            PeriodStartOn = periodStartOn,
            PeriodEndOn = periodEndOn,
            SourceAccountId = sourceAccountId,
            DistributableAmount = distributable,
            Shares = shares,
            Exclusions = [.. exclusions.OrderBy(exclusion => exclusion.HolderId)],
        };
    }

    private async Task<FinanceDistributionResult> ReplayOrConflictAsync(
        Guid confirmationToken,
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        CancellationToken cancellationToken)
    {
        FinanceDistribution? byToken = await dbContext.FinanceDistributions
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.ConfirmationToken == confirmationToken, cancellationToken);
        if (byToken is not null)
        {
            return new FinanceDistributionResult
            {
                Outcome = FinanceDistributionOutcome.ReplayedExistingExecution,
                DistributionId = byToken.Id,
            };
        }

        // A different confirmation token lost the race for the same period.
        return new FinanceDistributionResult { Outcome = FinanceDistributionOutcome.AlreadyDistributedForPeriod };
    }

    private static FinanceDistributionOutcome MapPlanOutcome(FinanceDistributionPlanOutcome outcome) => outcome switch
    {
        FinanceDistributionPlanOutcome.NothingToDistribute => FinanceDistributionOutcome.NothingToDistribute,
        FinanceDistributionPlanOutcome.NoEligiblePartners => FinanceDistributionOutcome.NoEligiblePartners,
        FinanceDistributionPlanOutcome.SharesDoNotSumToTotal => FinanceDistributionOutcome.SharesDoNotSumToTotal,
        FinanceDistributionPlanOutcome.SourceAccountNotFound => FinanceDistributionOutcome.SourceAccountNotFound,
        FinanceDistributionPlanOutcome.SourceAccountClosed => FinanceDistributionOutcome.SourceAccountClosed,
        FinanceDistributionPlanOutcome.AlreadyDistributedForPeriod =>
            FinanceDistributionOutcome.AlreadyDistributedForPeriod,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static FinanceDistributionPlan EmptyPlan(
        FinanceDistributionPlanOutcome outcome,
        IReadOnlyList<FinanceDistributionExclusion>? exclusions = null) => new()
        {
            Outcome = outcome,
            Shares = [],
            Exclusions = exclusions ?? [],
        };

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>SHA-256 hex over the canonical plan, excluding the token and hash themselves.</summary>
    private static string ComputeHash(FinanceDistributionPlan plan)
    {
        var canonical = new
        {
            plan.PeriodStartOn,
            plan.PeriodEndOn,
            plan.SourceAccountId,
            plan.DistributableAmount,
            Shares = plan.Shares
                .OrderBy(share => share.HolderId)
                .Select(share => new
                {
                    share.HolderId,
                    share.ShareBasisPoints,
                    share.ExactShareMinorUnits,
                    share.AllocatedAmount,
                    share.RemainderUnitAwarded,
                }),
            Exclusions = plan.Exclusions
                .OrderBy(exclusion => exclusion.HolderId)
                .Select(exclusion => new { exclusion.HolderId, exclusion.Reason }),
        };

        byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, SerializerOptions));
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }

    private static string Snapshot(FinanceDistribution distribution) => JsonSerializer.Serialize(
        new
        {
            distribution.Id,
            distribution.PeriodStartOn,
            distribution.PeriodEndOn,
            distribution.SourceFinanceAccountId,
            distribution.DistributableAmount,
            distribution.Status,
            distribution.ReversalReason,
        },
        SerializerOptions);

    private static FinanceDistributionListItem Project(FinanceDistribution distribution) => new()
    {
        DistributionId = distribution.Id,
        PeriodStartOn = distribution.PeriodStartOn,
        PeriodEndOn = distribution.PeriodEndOn,
        SourceFinanceAccountId = distribution.SourceFinanceAccountId,
        DistributableAmount = distribution.DistributableAmount,
        Status = distribution.Status,
        ExecutedAtUtc = distribution.ExecutedAtUtc,
    };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
