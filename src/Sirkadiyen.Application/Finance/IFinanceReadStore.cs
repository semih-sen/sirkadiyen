using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

public sealed record FinanceAccountHolderListItem
{
    public required Guid HolderId { get; init; }

    public required string DisplayName { get; init; }

    public Guid? UserId { get; init; }

    public required int ShareBasisPoints { get; init; }

    public required FinanceAccountHolderStatus Status { get; init; }
}

public sealed record FinanceAccountListItem
{
    public required Guid AccountId { get; init; }

    public required Guid FinanceAccountHolderId { get; init; }

    public required string HolderDisplayName { get; init; }

    public required string Name { get; init; }

    public required FinanceAccountKind Kind { get; init; }

    public required string CurrencyCode { get; init; }

    public required FinanceAccountStatus Status { get; init; }

    public required DateOnly OpenedOn { get; init; }

    public required decimal CurrentBalance { get; init; }

    public required DateOnly BalanceAsOfOn { get; init; }
}

public sealed record FinanceTransactionListItemEntry
{
    public required Guid FinanceAccountId { get; init; }

    public required string AccountName { get; init; }

    public required FinanceLedgerLeg Leg { get; init; }

    public required decimal Amount { get; init; }
}

public sealed record FinanceTransactionListItem
{
    public required Guid TransactionId { get; init; }

    public required FinanceTransactionKind Kind { get; init; }

    public FinanceCategory? Category { get; init; }

    public required decimal Amount { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string Description { get; init; }

    public string? Reference { get; init; }

    public string? CounterpartyName { get; init; }

    public required int RevisionNumber { get; init; }

    public required IReadOnlyList<FinanceTransactionListItemEntry> Entries { get; init; }
}

public sealed record FinanceTransactionDetail
{
    public required FinanceTransactionListItem Transaction { get; init; }

    public required uint RowVersion { get; init; }

    public required Guid CreatedByUserId { get; init; }

    public required string CreatedByEmail { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required Guid UpdatedByUserId { get; init; }

    public required string UpdatedByEmail { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record FinanceTransactionQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public DateOnly? FromOn { get; init; }

    public DateOnly? ToOn { get; init; }

    public FinanceTransactionKind? Kind { get; init; }

    public FinanceCategory? Category { get; init; }

    public Guid? AccountId { get; init; }

    public Guid? HolderId { get; init; }

    public string? Search { get; init; }
}

/// <summary>Read-only listings over accounts and transactions, with account balance derived on read.</summary>
public interface IFinanceReadStore
{
    Task<IReadOnlyList<FinanceAccountHolderListItem>> ListHoldersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceAccountListItem>> ListAccountsAsync(
        DateOnly asOfOn,
        CancellationToken cancellationToken);

    Task<FinanceAccountListItem?> FindAccountAsync(
        Guid accountId,
        DateOnly asOfOn,
        CancellationToken cancellationToken);

    Task<PagedResult<FinanceTransactionListItem>> ListTransactionsAsync(
        FinanceTransactionQuery query,
        CancellationToken cancellationToken);

    Task<FinanceTransactionDetail?> FindTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken);
}
