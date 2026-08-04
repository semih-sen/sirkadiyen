using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>Transactional PostgreSQL store for finance obligations and their settlements.</summary>
public sealed class FinanceObligationStore(SirkadiyenDbContext dbContext) : IFinanceObligationStore
{
    private const string SubjectType = "FinanceObligation";

    private const int MaximumPageSize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();

    public Task<FinanceObligationMutationResult> CreateAsync(
        FinanceObligationDirection direction,
        FinanceCategory category,
        string counterpartyName,
        string? description,
        decimal amount,
        DateOnly issuedOn,
        DateOnly? dueOn,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceObligation obligation = FinanceObligation.Create(
                direction,
                category,
                counterpartyName,
                description,
                amount,
                issuedOn,
                dueOn,
                actorUserId,
                actorEmail,
                nowUtc);

            dbContext.FinanceObligations.Add(obligation);
            dbContext.FinanceAudits.Add(FinanceAudit.Create(
                FinanceAuditAction.ObligationCreated,
                SubjectType,
                obligation.Id,
                actorUserId,
                actorEmail,
                nowUtc,
                correlationId: null,
                reason: null,
                beforeState: null,
                Snapshot(obligation),
                changedFields: null,
                amountDelta: 0m,
                revisionNumber: 1));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new FinanceObligationMutationResult
            {
                Outcome = FinanceObligationOutcome.Created,
                ObligationId = obligation.Id,
            };
        });

    public Task<FinanceObligationMutationResult> SettleAsync(
        Guid obligationId,
        Guid accountId,
        decimal amount,
        DateOnly settledOn,
        string? reference,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction dbTransaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceObligation? obligation = await dbContext.FinanceObligations
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_obligations
                    WHERE "Id" = {obligationId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (obligation is null)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult { Outcome = FinanceObligationOutcome.NotFound };
            }

            FinanceAccount? account = await dbContext.FinanceAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);
            if (account is null)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult { Outcome = FinanceObligationOutcome.AccountNotFound };
            }

            if (account.Status == FinanceAccountStatus.Closed)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult { Outcome = FinanceObligationOutcome.AccountClosed };
            }

            string beforeSnapshot = Snapshot(obligation);
            string counterpartyName = obligation.CounterpartyName;

            FinancePosting posting = obligation.Direction == FinanceObligationDirection.Receivable
                ? FinanceTransaction.RecordIncome(
                    accountId,
                    amount,
                    obligation.Category,
                    settledOn,
                    $"Settlement: {counterpartyName}",
                    reference,
                    counterpartyName,
                    actorUserId,
                    actorEmail,
                    nowUtc)
                : FinanceTransaction.RecordExpense(
                    accountId,
                    amount,
                    obligation.Category,
                    settledOn,
                    $"Settlement: {counterpartyName}",
                    reference,
                    counterpartyName,
                    actorUserId,
                    actorEmail,
                    nowUtc);

            try
            {
                obligation.RecordSettlement(amount, nowUtc);
            }
            catch (InvalidOperationException)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult
                {
                    Outcome = FinanceObligationOutcome.OverSettlement,
                    ObligationId = obligationId,
                };
            }

            FinanceSettlement settlement = FinanceSettlement.Create(
                obligationId,
                posting.Transaction.Id,
                obligation.Direction,
                amount,
                settledOn,
                nowUtc);

            dbContext.FinanceTransactions.Add(posting.Transaction);
            dbContext.FinanceLedgerEntries.AddRange(posting.Entries);
            dbContext.FinanceSettlements.Add(settlement);

            FinanceTransactionSnapshot transactionAfter =
                FinanceSnapshotSerializer.Capture(posting.Transaction, posting.Entries);
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
                FinanceSnapshotSerializer.Serialize(transactionAfter),
                changedFields: null,
                FinanceSnapshotSerializer.AmountDelta(null, transactionAfter),
                posting.Transaction.RevisionNumber));
            dbContext.FinanceAudits.Add(FinanceAudit.Create(
                FinanceAuditAction.ObligationSettled,
                SubjectType,
                obligationId,
                actorUserId,
                actorEmail,
                nowUtc,
                correlationId,
                reason: null,
                beforeSnapshot,
                Snapshot(obligation),
                changedFields: ["Status", "SettledAmount"],
                amountDelta: amount,
                revisionNumber: 1));

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new FinanceObligationMutationResult
            {
                Outcome = FinanceObligationOutcome.Settled,
                ObligationId = obligationId,
                SettlementId = settlement.Id,
                TransactionId = posting.Transaction.Id,
            };
        });

    public Task<FinanceObligationMutationResult> CancelSettlementAsync(
        Guid obligationId,
        Guid settlementId,
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

            FinanceObligation? obligation = await dbContext.FinanceObligations
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_obligations
                    WHERE "Id" = {obligationId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (obligation is null)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult { Outcome = FinanceObligationOutcome.NotFound };
            }

            FinanceSettlement? settlement = await dbContext.FinanceSettlements
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == settlementId && candidate.FinanceObligationId == obligationId,
                    cancellationToken);
            if (settlement is null)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult
                {
                    Outcome = FinanceObligationOutcome.SettlementNotFound,
                    ObligationId = obligationId,
                };
            }

            string beforeSnapshot = Snapshot(obligation);

            try
            {
                obligation.CancelSettlement(settlement.Amount, nowUtc);
            }
            catch (InvalidOperationException)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult
                {
                    Outcome = FinanceObligationOutcome.NothingSettledToCancel,
                    ObligationId = obligationId,
                };
            }

            dbContext.FinanceSettlements.Remove(settlement);
            dbContext.FinanceAudits.Add(FinanceAudit.Create(
                FinanceAuditAction.ObligationSettlementCancelled,
                SubjectType,
                obligationId,
                actorUserId,
                actorEmail,
                nowUtc,
                correlationId,
                reason,
                beforeSnapshot,
                Snapshot(obligation),
                changedFields: ["Status", "SettledAmount"],
                amountDelta: -settlement.Amount,
                revisionNumber: 1));

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new FinanceObligationMutationResult
            {
                Outcome = FinanceObligationOutcome.SettlementCancelled,
                ObligationId = obligationId,
            };
        });

    public Task<FinanceObligationMutationResult> WriteOffAsync(
        Guid obligationId,
        string reason,
        DateOnly writtenOffOn,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        CloseAsync(
            obligationId,
            reason,
            actorUserId,
            actorEmail,
            correlationId,
            nowUtc,
            FinanceAuditAction.ObligationWrittenOff,
            FinanceObligationOutcome.WrittenOff,
            obligation => obligation.WriteOff(reason, writtenOffOn, nowUtc),
            cancellationToken);

    public Task<FinanceObligationMutationResult> CancelAsync(
        Guid obligationId,
        string reason,
        DateOnly cancelledOn,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        CloseAsync(
            obligationId,
            reason,
            actorUserId,
            actorEmail,
            correlationId,
            nowUtc,
            FinanceAuditAction.ObligationCancelled,
            FinanceObligationOutcome.Cancelled,
            obligation => obligation.Cancel(reason, cancelledOn, nowUtc),
            cancellationToken);

    private Task<FinanceObligationMutationResult> CloseAsync(
        Guid obligationId,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        FinanceAuditAction action,
        FinanceObligationOutcome successOutcome,
        Action<FinanceObligation> close,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction dbTransaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceObligation? obligation = await dbContext.FinanceObligations
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_obligations
                    WHERE "Id" = {obligationId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (obligation is null)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult { Outcome = FinanceObligationOutcome.NotFound };
            }

            string beforeSnapshot = Snapshot(obligation);

            try
            {
                close(obligation);
            }
            catch (InvalidOperationException)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceObligationMutationResult
                {
                    Outcome = FinanceObligationOutcome.AlreadyClosed,
                    ObligationId = obligationId,
                };
            }

            dbContext.FinanceAudits.Add(FinanceAudit.Create(
                action,
                SubjectType,
                obligationId,
                actorUserId,
                actorEmail,
                nowUtc,
                correlationId,
                reason,
                beforeSnapshot,
                Snapshot(obligation),
                changedFields: ["Status"],
                amountDelta: 0m,
                revisionNumber: 1));

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new FinanceObligationMutationResult
            {
                Outcome = successOutcome,
                ObligationId = obligationId,
            };
        });

    public async Task<PagedResult<FinanceObligationListItem>> ListAsync(
        FinanceObligationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);

        IQueryable<FinanceObligation> obligations = dbContext.FinanceObligations.AsNoTracking();
        if (query.Direction is { } direction)
        {
            obligations = obligations.Where(obligation => obligation.Direction == direction);
        }

        if (query.Status is { } status)
        {
            obligations = obligations.Where(obligation => obligation.Status == status);
        }

        int totalCount = await obligations.CountAsync(cancellationToken);
        List<FinanceObligationListItem> items = await obligations
            .OrderByDescending(obligation => obligation.IssuedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(obligation => Project(obligation))
            .ToListAsync(cancellationToken);

        return new PagedResult<FinanceObligationListItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<FinanceObligationListItem?> FindAsync(
        Guid obligationId,
        CancellationToken cancellationToken) =>
        await dbContext.FinanceObligations
            .AsNoTracking()
            .Where(obligation => obligation.Id == obligationId)
            .Select(obligation => Project(obligation))
            .SingleOrDefaultAsync(cancellationToken);

    private static FinanceObligationListItem Project(FinanceObligation obligation) => new()
    {
        ObligationId = obligation.Id,
        Direction = obligation.Direction,
        Category = obligation.Category,
        CounterpartyName = obligation.CounterpartyName,
        Description = obligation.Description,
        Amount = obligation.Amount,
        SettledAmount = obligation.SettledAmount,
        IssuedOn = obligation.IssuedOn,
        DueOn = obligation.DueOn,
        Status = obligation.Status,
        RowVersion = obligation.RowVersion,
    };

    private static string Snapshot(FinanceObligation obligation) => JsonSerializer.Serialize(
        new
        {
            obligation.Id,
            obligation.Direction,
            obligation.Category,
            obligation.CounterpartyName,
            obligation.Amount,
            obligation.SettledAmount,
            obligation.Status,
            obligation.WrittenOffOn,
            obligation.CancelledOn,
            obligation.ClosureReason,
        },
        SerializerOptions);
}
