using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Api.Administration;

public sealed record CreateFinanceObligationRequest
{
    public required FinanceObligationDirection Direction { get; init; }

    public required FinanceCategory Category { get; init; }

    /// <example>A Corp</example>
    public required string? CounterpartyName { get; init; }

    public string? Description { get; init; }

    public required decimal Amount { get; init; }

    public required DateOnly IssuedOn { get; init; }

    public DateOnly? DueOn { get; init; }
}

public sealed record SettleFinanceObligationRequest
{
    public required Guid AccountId { get; init; }

    public required decimal Amount { get; init; }

    public required DateOnly SettledOn { get; init; }

    public string? Reference { get; init; }
}

public sealed record CancelFinanceObligationSettlementRequest
{
    /// <example>Wrong obligation was linked; relinking to the correct one.</example>
    public required string? Reason { get; init; }
}

public sealed record CloseFinanceObligationRequest
{
    public required DateOnly On { get; init; }

    /// <example>Counterparty confirmed as uncollectible.</example>
    public required string? Reason { get; init; }
}
