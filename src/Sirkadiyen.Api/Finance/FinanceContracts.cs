using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Api.Administration;

public sealed record CreateFinanceAccountHolderRequest
{
    /// <example>Ada Lovelace</example>
    public required string? DisplayName { get; init; }

    public Guid? UserId { get; init; }

    /// <example>5000</example>
    public required int ShareBasisPoints { get; init; }
}

public sealed record SetFinanceAccountHolderShareRequest
{
    public required int ShareBasisPoints { get; init; }
}

public sealed record OpenFinanceAccountRequest
{
    public required Guid FinanceAccountHolderId { get; init; }

    /// <example>Main cash box</example>
    public required string? Name { get; init; }

    public required FinanceAccountKind Kind { get; init; }

    public required DateOnly OpenedOn { get; init; }
}

public sealed record CloseFinanceAccountRequest
{
    public required string? Reason { get; init; }
}

public sealed record RecordOpeningBalanceRequest
{
    public required Guid AccountId { get; init; }

    /// <summary>May be negative.</summary>
    public required decimal SignedAmount { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string? Description { get; init; }
}

public sealed record RecordFinanceTransactionRequest
{
    public required Guid AccountId { get; init; }

    public required decimal Amount { get; init; }

    public required FinanceCategory? Category { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string? Description { get; init; }

    public string? Reference { get; init; }

    public string? CounterpartyName { get; init; }
}

public sealed record RecordFinanceTransferRequest
{
    public required Guid FromAccountId { get; init; }

    public required Guid ToAccountId { get; init; }

    public required decimal Amount { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string? Description { get; init; }

    public string? Reference { get; init; }
}

public sealed record UpdateFinanceTransactionRequest
{
    public required FinanceTransactionKind Kind { get; init; }

    public FinanceCategory? Category { get; init; }

    public required decimal Amount { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string? Description { get; init; }

    public string? Reference { get; init; }

    public string? CounterpartyName { get; init; }

    public required Guid AccountId { get; init; }

    public Guid? ToAccountId { get; init; }

    public required uint RowVersion { get; init; }

    /// <example>The amount was mistyped; corrected from bank statement.</example>
    public required string? Reason { get; init; }
}

public sealed record DeleteFinanceTransactionRequest
{
    public required uint RowVersion { get; init; }

    /// <example>Duplicate entry.</example>
    public required string? Reason { get; init; }
}
