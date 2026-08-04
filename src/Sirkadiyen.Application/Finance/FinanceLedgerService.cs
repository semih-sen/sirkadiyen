using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

/// <summary>Thin orchestration over <see cref="IFinanceLedgerStore"/>, owning the clock.</summary>
public sealed class FinanceLedgerService(IFinanceLedgerStore store, TimeProvider timeProvider)
{
    public Task<FinanceAccountHolderMutationResult> CreateHolderAsync(
        string displayName,
        Guid? userId,
        int shareBasisPoints,
        CancellationToken cancellationToken) =>
        store.CreateHolderAsync(displayName, userId, shareBasisPoints, timeProvider.GetUtcNow(), cancellationToken);

    public Task<FinanceAccountHolderMutationResult> SetHolderShareAsync(
        Guid holderId,
        int shareBasisPoints,
        CancellationToken cancellationToken) =>
        store.SetHolderShareAsync(holderId, shareBasisPoints, timeProvider.GetUtcNow(), cancellationToken);

    public Task<FinanceAccountHolderMutationResult> DeactivateHolderAsync(
        Guid holderId,
        CancellationToken cancellationToken) =>
        store.DeactivateHolderAsync(holderId, timeProvider.GetUtcNow(), cancellationToken);

    public Task<FinanceAccountMutationResult> OpenAccountAsync(
        Guid financeAccountHolderId,
        string name,
        FinanceAccountKind kind,
        DateOnly openedOn,
        CancellationToken cancellationToken) =>
        store.OpenAccountAsync(
            financeAccountHolderId,
            name,
            kind,
            openedOn,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<FinanceAccountMutationResult> CloseAccountAsync(
        Guid accountId,
        string reason,
        CancellationToken cancellationToken) =>
        store.CloseAccountAsync(accountId, reason, timeProvider.GetUtcNow(), cancellationToken);

    public Task<FinanceTransactionMutationResult> RecordOpeningBalanceAsync(
        Guid accountId,
        decimal signedAmount,
        DateOnly occurredOn,
        string description,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.RecordOpeningBalanceAsync(
            accountId,
            signedAmount,
            occurredOn,
            description,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

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
        CancellationToken cancellationToken) =>
        store.RecordIncomeAsync(
            accountId,
            amount,
            category,
            occurredOn,
            description,
            reference,
            counterpartyName,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

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
        CancellationToken cancellationToken) =>
        store.RecordExpenseAsync(
            accountId,
            amount,
            category,
            occurredOn,
            description,
            reference,
            counterpartyName,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

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
        CancellationToken cancellationToken) =>
        store.RecordTransferAsync(
            fromAccountId,
            toAccountId,
            amount,
            occurredOn,
            description,
            reference,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<FinanceTransactionMutationResult> UpdateTransactionAsync(
        Guid transactionId,
        FinanceTransactionEdit edit,
        uint expectedRowVersion,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.UpdateTransactionAsync(
            transactionId,
            edit,
            expectedRowVersion,
            reason,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<FinanceTransactionMutationResult> DeleteTransactionAsync(
        Guid transactionId,
        uint expectedRowVersion,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        store.DeleteTransactionAsync(
            transactionId,
            expectedRowVersion,
            reason,
            actorUserId,
            actorEmail,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
}
