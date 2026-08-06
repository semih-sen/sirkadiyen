namespace Sirkadiyen.Domain.Finance;

/// <summary>
/// What is owed, tracked in an accrual layer beside the cash-basis ledger. An obligation posts no
/// ledger entries of its own (ADR-092 §1); settling one writes an ordinary Income/Expense
/// transaction plus a <see cref="FinanceSettlement"/> linking the two. <see cref="SettledAmount"/>
/// is a write guard for this row, not a reporting source — a historical period's Receivables figure
/// must read <c>finance_settlements</c> as of that period end, never this field.
/// </summary>
public sealed class FinanceObligation
{
    public const int MaximumCounterpartyNameLength = 200;

    public const int MaximumDescriptionLength = 500;

    public const int MaximumClosureReasonLength = 2000;

    public const int MaximumActorEmailLength = 320;

    private FinanceObligation()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public FinanceObligationDirection Direction { get; private init; }

    public FinanceCategory Category { get; private init; }

    public string CounterpartyName { get; private init; } = string.Empty;

    public string? Description { get; private init; }

    public decimal Amount { get; private init; }

    public decimal SettledAmount { get; private set; }

    public DateOnly IssuedOn { get; private init; }

    public DateOnly? DueOn { get; private init; }

    public FinanceObligationStatus Status { get; private set; }

    public DateOnly? WrittenOffOn { get; private set; }

    public DateOnly? CancelledOn { get; private set; }

    public string? ClosureReason { get; private set; }

    public Guid CreatedByUserId { get; private init; }

    public string CreatedByEmail { get; private init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public decimal RemainingAmount => Amount - SettledAmount;

    public static FinanceObligation Create(
        FinanceObligationDirection direction,
        FinanceCategory category,
        string counterpartyName,
        string? description,
        decimal amount,
        DateOnly issuedOn,
        DateOnly? dueOn,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc)
    {
        bool categoryValid = direction == FinanceObligationDirection.Receivable
            ? FinanceCategories.IsIncome(category)
            : FinanceCategories.IsExpense(category);
        if (!categoryValid)
        {
            throw new ArgumentException(
                $"'{category}' does not match the {direction} direction.",
                nameof(category));
        }

        if (dueOn is not null && dueOn < issuedOn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dueOn),
                dueOn,
                "A due date cannot precede the issue date.");
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }

        counterpartyName = RequiredBounded(
            counterpartyName,
            MaximumCounterpartyNameLength,
            nameof(counterpartyName));
        description = OptionalBounded(description, MaximumDescriptionLength, nameof(description));
        amount = FinanceAmount.RequirePositive(amount, nameof(amount));
        actorEmail = RequiredBounded(actorEmail, MaximumActorEmailLength, nameof(actorEmail));

        return new FinanceObligation
        {
            Id = Guid.CreateVersion7(),
            Direction = direction,
            Category = category,
            CounterpartyName = counterpartyName,
            Description = description,
            Amount = amount,
            SettledAmount = 0m,
            IssuedOn = issuedOn,
            DueOn = dueOn,
            Status = FinanceObligationStatus.Open,
            CreatedByUserId = actorUserId,
            CreatedByEmail = actorEmail,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void RecordSettlement(decimal amount, DateTimeOffset updatedAtUtc)
    {
        if (Status is FinanceObligationStatus.WrittenOff or FinanceObligationStatus.Cancelled)
        {
            throw new InvalidOperationException($"A {Status} obligation cannot be settled.");
        }

        amount = FinanceAmount.RequirePositive(amount, nameof(amount));
        decimal newSettled = SettledAmount + amount;
        if (newSettled > Amount)
        {
            throw new InvalidOperationException(
                "The settlement would exceed the obligation's remaining amount.");
        }

        SettledAmount = newSettled;
        Status = newSettled == Amount
            ? FinanceObligationStatus.Settled
            : FinanceObligationStatus.PartiallySettled;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void CancelSettlement(decimal amount, DateTimeOffset updatedAtUtc)
    {
        if (Status is not (FinanceObligationStatus.PartiallySettled or FinanceObligationStatus.Settled))
        {
            throw new InvalidOperationException(
                "Only a partially or fully settled obligation has a settlement to cancel.");
        }

        amount = FinanceAmount.RequirePositive(amount, nameof(amount));
        if (amount > SettledAmount)
        {
            throw new InvalidOperationException("Cannot cancel more than what has been settled.");
        }

        SettledAmount -= amount;
        Status = SettledAmount == 0
            ? FinanceObligationStatus.Open
            : FinanceObligationStatus.PartiallySettled;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void WriteOff(string reason, DateOnly writtenOffOn, DateTimeOffset updatedAtUtc)
    {
        RequireOpenForClosure();
        ClosureReason = RequiredBounded(reason, MaximumClosureReasonLength, nameof(reason));
        WrittenOffOn = writtenOffOn;
        Status = FinanceObligationStatus.WrittenOff;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Cancel(string reason, DateOnly cancelledOn, DateTimeOffset updatedAtUtc)
    {
        if (SettledAmount != 0)
        {
            throw new InvalidOperationException(
                "An obligation with an active settlement cannot be cancelled; cancel the settlement first.");
        }

        RequireOpenForClosure();
        ClosureReason = RequiredBounded(reason, MaximumClosureReasonLength, nameof(reason));
        CancelledOn = cancelledOn;
        Status = FinanceObligationStatus.Cancelled;
        UpdatedAtUtc = updatedAtUtc;
    }

    private void RequireOpenForClosure()
    {
        if (Status is FinanceObligationStatus.WrittenOff or FinanceObligationStatus.Cancelled)
        {
            throw new InvalidOperationException($"The obligation is already {Status}.");
        }
    }

    private static string RequiredBounded(string? value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }

    private static string? OptionalBounded(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}
