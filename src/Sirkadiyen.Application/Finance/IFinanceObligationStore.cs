using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

public enum FinanceObligationOutcome
{
    Created,
    Settled,
    SettlementCancelled,
    WrittenOff,
    Cancelled,
    NotFound,
    SettlementNotFound,
    AlreadyClosed,
    OverSettlement,
    NothingSettledToCancel,
    AccountNotFound,
    AccountClosed,
    ConcurrentUpdate,
}

public sealed record FinanceObligationMutationResult
{
    public required FinanceObligationOutcome Outcome { get; init; }

    public Guid? ObligationId { get; init; }

    public Guid? SettlementId { get; init; }

    public Guid? TransactionId { get; init; }
}

public sealed record FinanceObligationListItem
{
    public required Guid ObligationId { get; init; }

    public required FinanceObligationDirection Direction { get; init; }

    public required FinanceCategory Category { get; init; }

    public required string CounterpartyName { get; init; }

    public string? Description { get; init; }

    public required decimal Amount { get; init; }

    public required decimal SettledAmount { get; init; }

    public required DateOnly IssuedOn { get; init; }

    public DateOnly? DueOn { get; init; }

    public required FinanceObligationStatus Status { get; init; }

    public required uint RowVersion { get; init; }
}

public sealed record FinanceObligationQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public FinanceObligationDirection? Direction { get; init; }

    public FinanceObligationStatus? Status { get; init; }
}

/// <summary>
/// Obligations are a full accrual layer beside the cash-basis ledger. An obligation posts no ledger
/// entries of its own: settling one writes an ordinary Income/Expense transaction plus a
/// <see cref="FinanceSettlement"/> linking obligation to transaction, which is what keeps double
/// counting structurally impossible (ADR-093). Cancelling a settlement un-links it without touching
/// the cash transaction it produced — that transaction is money that was actually received or paid,
/// and remains on the books; only its attribution to this obligation is undone.
/// </summary>
public interface IFinanceObligationStore
{
    Task<FinanceObligationMutationResult> CreateAsync(
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
        CancellationToken cancellationToken);

    Task<FinanceObligationMutationResult> SettleAsync(
        Guid obligationId,
        Guid accountId,
        decimal amount,
        DateOnly settledOn,
        string? reference,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceObligationMutationResult> CancelSettlementAsync(
        Guid obligationId,
        Guid settlementId,
        string reason,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceObligationMutationResult> WriteOffAsync(
        Guid obligationId,
        string reason,
        DateOnly writtenOffOn,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<FinanceObligationMutationResult> CancelAsync(
        Guid obligationId,
        string reason,
        DateOnly cancelledOn,
        Guid actorUserId,
        string actorEmail,
        string? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<PagedResult<FinanceObligationListItem>> ListAsync(
        FinanceObligationQuery query,
        CancellationToken cancellationToken);

    Task<FinanceObligationListItem?> FindAsync(Guid obligationId, CancellationToken cancellationToken);
}
