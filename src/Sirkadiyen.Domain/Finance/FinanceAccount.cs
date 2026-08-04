namespace Sirkadiyen.Domain.Finance;

/// <summary>
/// A cash box or bank account belonging to one <see cref="FinanceAccountHolder"/>. Its balance is
/// never stored here — it is derived from <c>finance_ledger_entries</c> (ADR-092 §2). This row is
/// what a store locks with <c>FOR UPDATE</c> before debiting: derived balance, real lock target.
/// </summary>
public sealed class FinanceAccount
{
    public const int MaximumNameLength = 120;

    public const int MaximumClosedReasonLength = 2000;

    public const string SupportedCurrencyCode = "TRY";

    private FinanceAccount()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid FinanceAccountHolderId { get; private init; }

    public string Name { get; private set; } = string.Empty;

    public FinanceAccountKind Kind { get; private init; }

    public string CurrencyCode { get; private init; } = SupportedCurrencyCode;

    public FinanceAccountStatus Status { get; private set; }

    public DateOnly OpenedOn { get; private init; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public string? ClosedReason { get; private set; }

    public uint RowVersion { get; private set; }

    public static FinanceAccount Open(
        Guid financeAccountHolderId,
        string name,
        FinanceAccountKind kind,
        DateOnly openedOn,
        DateTimeOffset createdAtUtc)
    {
        if (financeAccountHolderId == Guid.Empty)
        {
            throw new ArgumentException("A holder is required.", nameof(financeAccountHolderId));
        }

        name = RequiredBounded(name, MaximumNameLength, nameof(name));

        return new FinanceAccount
        {
            Id = Guid.CreateVersion7(),
            FinanceAccountHolderId = financeAccountHolderId,
            Name = name,
            Kind = kind,
            CurrencyCode = SupportedCurrencyCode,
            Status = FinanceAccountStatus.Active,
            OpenedOn = openedOn,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public void Rename(string name)
    {
        Name = RequiredBounded(name, MaximumNameLength, nameof(name));
    }

    public void Close(string reason, DateTimeOffset closedAtUtc)
    {
        if (Status == FinanceAccountStatus.Closed)
        {
            throw new InvalidOperationException("The account is already closed.");
        }

        Status = FinanceAccountStatus.Closed;
        ClosedReason = RequiredBounded(reason, MaximumClosedReasonLength, nameof(reason));
        ClosedAtUtc = closedAtUtc;
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}

public enum FinanceAccountKind
{
    Cash,
    Bank,
}

public enum FinanceAccountStatus
{
    Active,
    Closed,
}
