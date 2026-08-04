using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

public enum FinanceAccountHolderOutcome
{
    Created,
    ShareSet,
    Deactivated,
    NotFound,
    DuplicateDisplayName,
    AlreadyInactive,
    ConcurrentUpdate,
}

public sealed record FinanceAccountHolderMutationResult
{
    public required FinanceAccountHolderOutcome Outcome { get; init; }

    public Guid? HolderId { get; init; }
}

public enum FinanceAccountOutcome
{
    Opened,
    Closed,
    NotFound,
    HolderNotFound,
    AlreadyClosed,
    DuplicateName,
    ConcurrentUpdate,
}

public sealed record FinanceAccountMutationResult
{
    public required FinanceAccountOutcome Outcome { get; init; }

    public Guid? AccountId { get; init; }
}

public enum FinanceTransactionOutcome
{
    Recorded,
    Updated,
    Deleted,
    NotFound,
    AccountNotFound,
    AccountClosed,
    CategoryMismatch,
    SameAccount,
    InsufficientBalance,
    TransactionSettlesAnObligation,
    TransactionIsADistributionPayout,
    KindChangeNotAllowed,
    ConcurrentUpdate,
}

public sealed record FinanceTransactionMutationResult
{
    public required FinanceTransactionOutcome Outcome { get; init; }

    public Guid? TransactionId { get; init; }
}

/// <summary>
/// Transactional writes over the finance ledger: holders, accounts, and transactions (including
/// edit and hard delete). Every multi-row write commits inside one <c>RetriableTransaction</c> and
/// writes its <see cref="FinanceAudit"/> row in the same commit (ADR-092).
/// </summary>
public interface IFinanceLedgerStore
{
    Task<FinanceAccountHolderMutationResult> CreateHolderAsync(
        string displayName,
        Guid? userId,
        int shareBasisPoints,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceAccountHolderMutationResult> SetHolderShareAsync(
        Guid holderId,
        int shareBasisPoints,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceAccountHolderMutationResult> DeactivateHolderAsync(
        Guid holderId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceAccountMutationResult> OpenAccountAsync(
        Guid financeAccountHolderId,
        string name,
        FinanceAccountKind kind,
        DateOnly openedOn,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceAccountMutationResult> CloseAccountAsync(
        Guid accountId,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceTransactionMutationResult> RecordOpeningBalanceAsync(
        Guid accountId,
        decimal signedAmount,
        DateOnly occurredOn,
        string description,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceTransactionMutationResult> RecordIncomeAsync(
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
        CancellationToken cancellationToken);

    Task<FinanceTransactionMutationResult> RecordExpenseAsync(
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
        CancellationToken cancellationToken);

    Task<FinanceTransactionMutationResult> RecordTransferAsync(
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
        CancellationToken cancellationToken);

    Task<FinanceTransactionMutationResult> UpdateTransactionAsync(
        Guid transactionId,
        FinanceTransactionEdit edit,
        uint expectedRowVersion,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceTransactionMutationResult> DeleteTransactionAsync(
        Guid transactionId,
        uint expectedRowVersion,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
