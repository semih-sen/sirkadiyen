using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Stores;

/// <summary>Transactional PostgreSQL store for finance holders, accounts, and transactions.</summary>
public sealed class FinanceLedgerStore(SirkadiyenDbContext dbContext) : IFinanceLedgerStore
{
    private const string SubjectType = "FinanceTransaction";

    public async Task<FinanceAccountHolderMutationResult> CreateHolderAsync(
        string displayName,
        Guid? userId,
        int shareBasisPoints,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        FinanceAccountHolder holder = FinanceAccountHolder.Create(
            displayName,
            userId,
            shareBasisPoints,
            nowUtc);

        try
        {
            await RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                dbContext.FinanceAccountHolders.Add(holder);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new FinanceAccountHolderMutationResult
            {
                Outcome = FinanceAccountHolderOutcome.DuplicateDisplayName,
            };
        }

        return new FinanceAccountHolderMutationResult
        {
            Outcome = FinanceAccountHolderOutcome.Created,
            HolderId = holder.Id,
        };
    }

    public Task<FinanceAccountHolderMutationResult> SetHolderShareAsync(
        Guid holderId,
        int shareBasisPoints,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceAccountHolder? holder = await dbContext.FinanceAccountHolders
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_account_holders
                    WHERE "Id" = {holderId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (holder is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceAccountHolderMutationResult
                {
                    Outcome = FinanceAccountHolderOutcome.NotFound,
                };
            }

            try
            {
                holder.SetShare(shareBasisPoints, nowUtc);
            }
            catch (InvalidOperationException)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceAccountHolderMutationResult
                {
                    Outcome = FinanceAccountHolderOutcome.AlreadyInactive,
                    HolderId = holder.Id,
                };
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new FinanceAccountHolderMutationResult
            {
                Outcome = FinanceAccountHolderOutcome.ShareSet,
                HolderId = holder.Id,
            };
        });

    public Task<FinanceAccountHolderMutationResult> DeactivateHolderAsync(
        Guid holderId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceAccountHolder? holder = await dbContext.FinanceAccountHolders
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_account_holders
                    WHERE "Id" = {holderId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (holder is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceAccountHolderMutationResult
                {
                    Outcome = FinanceAccountHolderOutcome.NotFound,
                };
            }

            try
            {
                holder.Deactivate(nowUtc);
            }
            catch (InvalidOperationException)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceAccountHolderMutationResult
                {
                    Outcome = FinanceAccountHolderOutcome.AlreadyInactive,
                    HolderId = holder.Id,
                };
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new FinanceAccountHolderMutationResult
            {
                Outcome = FinanceAccountHolderOutcome.Deactivated,
                HolderId = holder.Id,
            };
        });

    public async Task<FinanceAccountMutationResult> OpenAccountAsync(
        Guid financeAccountHolderId,
        string name,
        FinanceAccountKind kind,
        DateOnly openedOn,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        FinanceAccount account = FinanceAccount.Open(financeAccountHolderId, name, kind, openedOn, nowUtc);

        try
        {
            return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                bool holderExists = await dbContext.FinanceAccountHolders
                    .AsNoTracking()
                    .AnyAsync(holder => holder.Id == financeAccountHolderId, cancellationToken);
                if (!holderExists)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new FinanceAccountMutationResult
                    {
                        Outcome = FinanceAccountOutcome.HolderNotFound,
                    };
                }

                dbContext.FinanceAccounts.Add(account);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new FinanceAccountMutationResult
                {
                    Outcome = FinanceAccountOutcome.Opened,
                    AccountId = account.Id,
                };
            });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new FinanceAccountMutationResult { Outcome = FinanceAccountOutcome.DuplicateName };
        }
    }

    public Task<FinanceAccountMutationResult> CloseAccountAsync(
        Guid accountId,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceAccount? account = await dbContext.FinanceAccounts
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_accounts
                    WHERE "Id" = {accountId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (account is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceAccountMutationResult { Outcome = FinanceAccountOutcome.NotFound };
            }

            try
            {
                account.Close(reason, nowUtc);
            }
            catch (InvalidOperationException)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceAccountMutationResult
                {
                    Outcome = FinanceAccountOutcome.AlreadyClosed,
                    AccountId = account.Id,
                };
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new FinanceAccountMutationResult
            {
                Outcome = FinanceAccountOutcome.Closed,
                AccountId = account.Id,
            };
        });

    public Task<FinanceTransactionMutationResult> RecordOpeningBalanceAsync(
        Guid accountId,
        decimal signedAmount,
        DateOnly occurredOn,
        string description,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceAccount? account = await FindAccountAsync(accountId, cancellationToken);
            FinanceTransactionMutationResult? guard = GuardAccount(account);
            if (guard is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return guard;
            }

            FinancePosting posting = FinanceTransaction.RecordOpeningBalance(
                accountId,
                signedAmount,
                occurredOn,
                description,
                actorUserId,
                actorEmail,
                nowUtc);

            return await SaveCreationAsync(
                posting,
                actorUserId,
                actorEmail,
                correlationId,
                nowUtc,
                transaction,
                cancellationToken);
        });

    public Task<FinanceTransactionMutationResult> RecordIncomeAsync(
        Guid accountId,
        decimal amount,
        FinanceCategory category,
        DateOnly occurredOn,
        string description,
        string? reference,
        string? counterpartyName,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (!FinanceCategories.IsIncome(category))
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.CategoryMismatch,
                };
            }

            FinanceAccount? account = await FindAccountAsync(accountId, cancellationToken);
            FinanceTransactionMutationResult? guard = GuardAccount(account);
            if (guard is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return guard;
            }

            FinancePosting posting = FinanceTransaction.RecordIncome(
                accountId,
                amount,
                category,
                occurredOn,
                description,
                reference,
                counterpartyName,
                actorUserId,
                actorEmail,
                nowUtc);

            return await SaveCreationAsync(
                posting,
                actorUserId,
                actorEmail,
                correlationId,
                nowUtc,
                transaction,
                cancellationToken);
        });

    public Task<FinanceTransactionMutationResult> RecordExpenseAsync(
        Guid accountId,
        decimal amount,
        FinanceCategory category,
        DateOnly occurredOn,
        string description,
        string? reference,
        string? counterpartyName,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (!FinanceCategories.IsExpense(category))
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.CategoryMismatch,
                };
            }

            FinanceAccount? account = await FindAccountAsync(accountId, cancellationToken);
            FinanceTransactionMutationResult? guard = GuardAccount(account);
            if (guard is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return guard;
            }

            FinancePosting posting = FinanceTransaction.RecordExpense(
                accountId,
                amount,
                category,
                occurredOn,
                description,
                reference,
                counterpartyName,
                actorUserId,
                actorEmail,
                nowUtc);

            return await SaveCreationAsync(
                posting,
                actorUserId,
                actorEmail,
                correlationId,
                nowUtc,
                transaction,
                cancellationToken);
        });

    public Task<FinanceTransactionMutationResult> RecordTransferAsync(
        Guid fromAccountId,
        Guid toAccountId,
        decimal amount,
        DateOnly occurredOn,
        string description,
        string? reference,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (fromAccountId == toAccountId)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.SameAccount,
                };
            }

            // Only the source account is locked: a transfer moves money the ledger claims exists,
            // so it (unlike an ordinary income/expense entry) must check the balance under a lock
            // that serializes against other transfers, edits, and deletes touching the same account.
            FinanceAccount? source = await LockAccountAsync(fromAccountId, cancellationToken);
            FinanceTransactionMutationResult? sourceGuard = GuardAccount(source);
            if (sourceGuard is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return sourceGuard;
            }

            FinanceAccount? destination = await FindAccountAsync(toAccountId, cancellationToken);
            FinanceTransactionMutationResult? destinationGuard = GuardAccount(destination);
            if (destinationGuard is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return destinationGuard;
            }

            decimal balance = await GetBalanceAsync(fromAccountId, occurredOn, cancellationToken);
            if (balance < amount)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.InsufficientBalance,
                };
            }

            FinancePosting posting = FinanceTransaction.RecordTransfer(
                fromAccountId,
                toAccountId,
                amount,
                occurredOn,
                description,
                reference,
                actorUserId,
                actorEmail,
                nowUtc);

            return await SaveCreationAsync(
                posting,
                actorUserId,
                actorEmail,
                correlationId,
                nowUtc,
                transaction,
                cancellationToken);
        });

    public Task<FinanceTransactionMutationResult> UpdateTransactionAsync(
        Guid transactionId,
        FinanceTransactionEdit edit,
        uint expectedRowVersion,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction dbTransaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            FinanceTransaction? financeTransaction = await dbContext.FinanceTransactions
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_transactions
                    WHERE "Id" = {transactionId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (financeTransaction is null)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult { Outcome = FinanceTransactionOutcome.NotFound };
            }

            if (financeTransaction.RowVersion != expectedRowVersion)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.ConcurrentUpdate,
                };
            }

            if (financeTransaction.Kind == FinanceTransactionKind.Distribution)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.TransactionIsADistributionPayout,
                };
            }

            if (financeTransaction.Kind == FinanceTransactionKind.OpeningBalance
                || edit.Kind is FinanceTransactionKind.OpeningBalance or FinanceTransactionKind.Distribution)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.KindChangeNotAllowed,
                };
            }

            if (edit.Kind == FinanceTransactionKind.Transfer && edit.AccountId == edit.ToAccountId)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult { Outcome = FinanceTransactionOutcome.SameAccount };
            }

            bool categoryValid = edit.Kind switch
            {
                FinanceTransactionKind.Income => edit.Category is { } income && FinanceCategories.IsIncome(income),
                FinanceTransactionKind.Expense => edit.Category is { } expense && FinanceCategories.IsExpense(expense),
                _ => true,
            };
            if (!categoryValid)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.CategoryMismatch,
                };
            }

            List<FinanceLedgerEntry> oldEntries = await dbContext.FinanceLedgerEntries
                .Where(entry => entry.FinanceTransactionId == transactionId)
                .ToListAsync(cancellationToken);
            FinanceTransactionSnapshot before = FinanceSnapshotSerializer.Capture(financeTransaction, oldEntries);

            List<Guid> involvedAccountIds =
            [
                .. new HashSet<Guid>(
                [
                    .. oldEntries.Select(entry => entry.FinanceAccountId),
                    edit.AccountId,
                    .. edit.ToAccountId is { } toAccountId ? [toAccountId] : Array.Empty<Guid>(),
                ]),
            ];
            involvedAccountIds.Sort();

            List<FinanceAccount> lockedAccounts = await dbContext.FinanceAccounts
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_accounts
                    WHERE "Id" = ANY({involvedAccountIds.ToArray()})
                    ORDER BY "Id"
                    FOR UPDATE
                    """)
                .ToListAsync(cancellationToken);

            if (lockedAccounts.Count != involvedAccountIds.Count)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.AccountNotFound,
                };
            }

            if (lockedAccounts.Any(account => account.Status == FinanceAccountStatus.Closed
                && (account.Id == edit.AccountId || account.Id == edit.ToAccountId)))
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.AccountClosed,
                };
            }

            IReadOnlyList<FinanceLedgerEntry> newEntries;
            try
            {
                newEntries = financeTransaction.Rewrite(edit, actorUserId, actorEmail, nowUtc);
            }
            catch (InvalidOperationException)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.KindChangeNotAllowed,
                };
            }

            dbContext.FinanceLedgerEntries.RemoveRange(oldEntries);
            dbContext.FinanceLedgerEntries.AddRange(newEntries);

            FinanceTransactionSnapshot after = FinanceSnapshotSerializer.Capture(financeTransaction, newEntries);
            FinanceAudit audit = FinanceAudit.Create(
                FinanceAuditAction.TransactionUpdated,
                SubjectType,
                transactionId,
                actorUserId,
                actorEmail,
                nowUtc,
                correlationId,
                reason,
                FinanceSnapshotSerializer.Serialize(before),
                FinanceSnapshotSerializer.Serialize(after),
                FinanceSnapshotSerializer.DiffChangedFields(before, after),
                FinanceSnapshotSerializer.AmountDelta(before, after),
                financeTransaction.RevisionNumber);
            dbContext.FinanceAudits.Add(audit);

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new FinanceTransactionMutationResult
            {
                Outcome = FinanceTransactionOutcome.Updated,
                TransactionId = transactionId,
            };
        });
    }

    public Task<FinanceTransactionMutationResult> DeleteTransactionAsync(
        Guid transactionId,
        uint expectedRowVersion,
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

            FinanceTransaction? financeTransaction = await dbContext.FinanceTransactions
                .FromSql($"""
                    SELECT *, xmin FROM sirkadiyen.finance_transactions
                    WHERE "Id" = {transactionId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (financeTransaction is null)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult { Outcome = FinanceTransactionOutcome.NotFound };
            }

            if (financeTransaction.RowVersion != expectedRowVersion)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.ConcurrentUpdate,
                };
            }

            if (financeTransaction.Kind == FinanceTransactionKind.Distribution)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.TransactionIsADistributionPayout,
                };
            }

            // A settlement-linked transaction is additionally refused once finance_settlements
            // exists (Phase 4); the FK there is Restrict regardless, so the database backstops this.
            List<FinanceLedgerEntry> entries = await dbContext.FinanceLedgerEntries
                .Where(entry => entry.FinanceTransactionId == transactionId)
                .ToListAsync(cancellationToken);
            FinanceTransactionSnapshot before = FinanceSnapshotSerializer.Capture(financeTransaction, entries);

            dbContext.FinanceLedgerEntries.RemoveRange(entries);
            dbContext.FinanceTransactions.Remove(financeTransaction);

            FinanceAudit audit = FinanceAudit.Create(
                FinanceAuditAction.TransactionDeleted,
                SubjectType,
                transactionId,
                actorUserId,
                actorEmail,
                nowUtc,
                correlationId,
                reason,
                FinanceSnapshotSerializer.Serialize(before),
                afterState: null,
                changedFields: null,
                FinanceSnapshotSerializer.AmountDelta(before, null),
                financeTransaction.RevisionNumber);
            dbContext.FinanceAudits.Add(audit);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
            {
                return new FinanceTransactionMutationResult
                {
                    Outcome = FinanceTransactionOutcome.TransactionSettlesAnObligation,
                };
            }

            await dbTransaction.CommitAsync(cancellationToken);
            return new FinanceTransactionMutationResult
            {
                Outcome = FinanceTransactionOutcome.Deleted,
                TransactionId = transactionId,
            };
        });

    private async Task<FinanceTransactionMutationResult> SaveCreationAsync(
        FinancePosting posting,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        dbContext.FinanceTransactions.Add(posting.Transaction);
        dbContext.FinanceLedgerEntries.AddRange(posting.Entries);

        FinanceTransactionSnapshot after = FinanceSnapshotSerializer.Capture(posting.Transaction, posting.Entries);
        FinanceAudit audit = FinanceAudit.Create(
            FinanceAuditAction.TransactionCreated,
            SubjectType,
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
            posting.Transaction.RevisionNumber);
        dbContext.FinanceAudits.Add(audit);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new FinanceTransactionMutationResult
        {
            Outcome = FinanceTransactionOutcome.Recorded,
            TransactionId = posting.Transaction.Id,
        };
    }

    private Task<FinanceAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        dbContext.FinanceAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken);

    private Task<FinanceAccount?> LockAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        dbContext.FinanceAccounts
            .FromSql($"""
                SELECT *, xmin FROM sirkadiyen.finance_accounts
                WHERE "Id" = {accountId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<decimal> GetBalanceAsync(
        Guid accountId,
        DateOnly asOfOn,
        CancellationToken cancellationToken)
    {
        decimal? sum = await dbContext.FinanceLedgerEntries
            .Where(entry => entry.FinanceAccountId == accountId && entry.OccurredOn <= asOfOn)
            .SumAsync(entry => (decimal?)entry.Amount, cancellationToken);
        return sum ?? 0m;
    }

    private static FinanceTransactionMutationResult? GuardAccount(FinanceAccount? account)
    {
        if (account is null)
        {
            return new FinanceTransactionMutationResult { Outcome = FinanceTransactionOutcome.AccountNotFound };
        }

        if (account.Status == FinanceAccountStatus.Closed)
        {
            return new FinanceTransactionMutationResult { Outcome = FinanceTransactionOutcome.AccountClosed };
        }

        return null;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}
