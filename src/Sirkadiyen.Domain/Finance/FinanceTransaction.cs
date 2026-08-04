namespace Sirkadiyen.Domain.Finance;

public enum FinanceTransactionKind
{
    OpeningBalance,
    Income,
    Expense,
    Transfer,
    Distribution,
}

public enum FinanceLedgerLeg
{
    Single,
    From,
    To,
}

/// <summary>
/// One signed posting against one account, produced by a <see cref="FinanceTransaction"/>. Account
/// balance is <c>SUM(Amount)</c> over these rows — never a stored column — which is what makes
/// editing and deleting a transaction safe (ADR-092 §2).
/// </summary>
public sealed class FinanceLedgerEntry
{
    private FinanceLedgerEntry()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid FinanceTransactionId { get; private init; }

    public Guid FinanceAccountId { get; private init; }

    /// <summary>Denormalized from the owning transaction so the balance aggregate is join-free.</summary>
    public FinanceTransactionKind Kind { get; private init; }

    public FinanceLedgerLeg Leg { get; private init; }

    public decimal Amount { get; private init; }

    /// <summary>Denormalized from the owning transaction so period aggregates are join-free.</summary>
    public DateOnly OccurredOn { get; private init; }

    internal static FinanceLedgerEntry Create(
        Guid financeTransactionId,
        Guid financeAccountId,
        FinanceTransactionKind kind,
        FinanceLedgerLeg leg,
        decimal amount,
        DateOnly occurredOn)
    {
        if (financeTransactionId == Guid.Empty)
        {
            throw new ArgumentException("A transaction is required.", nameof(financeTransactionId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("An account is required.", nameof(financeAccountId));
        }

        return new FinanceLedgerEntry
        {
            Id = Guid.CreateVersion7(),
            FinanceTransactionId = financeTransactionId,
            FinanceAccountId = financeAccountId,
            Kind = kind,
            Leg = leg,
            Amount = FinanceAmount.RequireNonZero(amount, nameof(amount)),
            OccurredOn = occurredOn,
        };
    }
}

/// <summary>A transaction together with the ledger entries it produced or now produces.</summary>
public sealed record FinancePosting(
    FinanceTransaction Transaction,
    IReadOnlyList<FinanceLedgerEntry> Entries);

/// <summary>The fields of a transaction that <see cref="FinanceTransaction.Rewrite"/> may change.</summary>
public sealed record FinanceTransactionEdit
{
    public required FinanceTransactionKind Kind { get; init; }

    public FinanceCategory? Category { get; init; }

    public required decimal Amount { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string Description { get; init; }

    public string? Reference { get; init; }

    public string? CounterpartyName { get; init; }

    /// <summary>The account for Income/Expense; the source (From) account for a Transfer.</summary>
    public required Guid AccountId { get; init; }

    /// <summary>The destination (To) account. Required for, and only meaningful for, a Transfer.</summary>
    public Guid? ToAccountId { get; init; }
}

/// <summary>
/// The editable business event behind one or more <see cref="FinanceLedgerEntry"/> postings
/// (ADR-092 §4). Transactions are editable and hard-deletable; correctness rests entirely on
/// <see cref="FinanceAudit"/>, built by the caller from the before/after images this type exposes.
/// </summary>
public sealed class FinanceTransaction
{
    public const int MaximumDescriptionLength = 500;

    public const int MaximumReferenceLength = 200;

    public const int MaximumCounterpartyNameLength = 200;

    public const int MaximumActorEmailLength = 320;

    private FinanceTransaction()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public FinanceTransactionKind Kind { get; private set; }

    public FinanceCategory? Category { get; private set; }

    /// <summary>The gross magnitude; always positive. Sign lives on the ledger entries.</summary>
    public decimal Amount { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public string? Reference { get; private set; }

    public string? CounterpartyName { get; private set; }

    public Guid? FinanceDistributionId { get; private init; }

    public int RevisionNumber { get; private set; }

    public Guid CreatedByUserId { get; private init; }

    public string CreatedByEmail { get; private init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public Guid UpdatedByUserId { get; private set; }

    public string UpdatedByEmail { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public static FinancePosting RecordOpeningBalance(
        Guid accountId,
        decimal signedAmount,
        DateOnly occurredOn,
        string description,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc)
    {
        signedAmount = FinanceAmount.RequireNonZero(signedAmount, nameof(signedAmount));

        FinanceTransaction transaction = BuildNew(
            FinanceTransactionKind.OpeningBalance,
            category: null,
            Math.Abs(signedAmount),
            occurredOn,
            description,
            reference: null,
            counterpartyName: null,
            financeDistributionId: null,
            actorUserId,
            actorEmail,
            nowUtc);

        FinanceLedgerEntry entry = FinanceLedgerEntry.Create(
            transaction.Id,
            accountId,
            transaction.Kind,
            FinanceLedgerLeg.Single,
            signedAmount,
            occurredOn);

        return new FinancePosting(transaction, [entry]);
    }

    public static FinancePosting RecordIncome(
        Guid accountId,
        decimal amount,
        FinanceCategory category,
        DateOnly occurredOn,
        string description,
        string? reference,
        string? counterpartyName,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc)
    {
        RequireCategoryKind(category, isIncome: true);
        amount = FinanceAmount.RequirePositive(amount, nameof(amount));

        FinanceTransaction transaction = BuildNew(
            FinanceTransactionKind.Income,
            category,
            amount,
            occurredOn,
            description,
            reference,
            counterpartyName,
            financeDistributionId: null,
            actorUserId,
            actorEmail,
            nowUtc);

        FinanceLedgerEntry entry = FinanceLedgerEntry.Create(
            transaction.Id,
            accountId,
            transaction.Kind,
            FinanceLedgerLeg.Single,
            amount,
            occurredOn);

        return new FinancePosting(transaction, [entry]);
    }

    public static FinancePosting RecordExpense(
        Guid accountId,
        decimal amount,
        FinanceCategory category,
        DateOnly occurredOn,
        string description,
        string? reference,
        string? counterpartyName,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc)
    {
        RequireCategoryKind(category, isIncome: false);
        amount = FinanceAmount.RequirePositive(amount, nameof(amount));

        FinanceTransaction transaction = BuildNew(
            FinanceTransactionKind.Expense,
            category,
            amount,
            occurredOn,
            description,
            reference,
            counterpartyName,
            financeDistributionId: null,
            actorUserId,
            actorEmail,
            nowUtc);

        FinanceLedgerEntry entry = FinanceLedgerEntry.Create(
            transaction.Id,
            accountId,
            transaction.Kind,
            FinanceLedgerLeg.Single,
            -amount,
            occurredOn);

        return new FinancePosting(transaction, [entry]);
    }

    public static FinancePosting RecordTransfer(
        Guid fromAccountId,
        Guid toAccountId,
        decimal amount,
        DateOnly occurredOn,
        string description,
        string? reference,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc)
    {
        if (fromAccountId == toAccountId)
        {
            throw new InvalidOperationException("A transfer cannot move money to the same account.");
        }

        amount = FinanceAmount.RequirePositive(amount, nameof(amount));

        FinanceTransaction transaction = BuildNew(
            FinanceTransactionKind.Transfer,
            category: null,
            amount,
            occurredOn,
            description,
            reference,
            counterpartyName: null,
            financeDistributionId: null,
            actorUserId,
            actorEmail,
            nowUtc);

        FinanceLedgerEntry from = FinanceLedgerEntry.Create(
            transaction.Id,
            fromAccountId,
            transaction.Kind,
            FinanceLedgerLeg.From,
            -amount,
            occurredOn);
        FinanceLedgerEntry to = FinanceLedgerEntry.Create(
            transaction.Id,
            toAccountId,
            transaction.Kind,
            FinanceLedgerLeg.To,
            amount,
            occurredOn);

        return new FinancePosting(transaction, [from, to]);
    }

    /// <summary>Outflow only: partners are paid externally, so no destination account is credited.</summary>
    public static FinancePosting RecordDistributionPayout(
        Guid sourceAccountId,
        Guid financeDistributionId,
        decimal amount,
        DateOnly occurredOn,
        string description,
        string counterpartyName,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc)
    {
        if (financeDistributionId == Guid.Empty)
        {
            throw new ArgumentException("A distribution is required.", nameof(financeDistributionId));
        }

        amount = FinanceAmount.RequirePositive(amount, nameof(amount));

        FinanceTransaction transaction = BuildNew(
            FinanceTransactionKind.Distribution,
            category: null,
            amount,
            occurredOn,
            description,
            reference: null,
            counterpartyName,
            financeDistributionId,
            actorUserId,
            actorEmail,
            nowUtc);

        FinanceLedgerEntry entry = FinanceLedgerEntry.Create(
            transaction.Id,
            sourceAccountId,
            transaction.Kind,
            FinanceLedgerLeg.Single,
            -amount,
            occurredOn);

        return new FinancePosting(transaction, [entry]);
    }

    /// <summary>
    /// Rewrites this transaction's fields in place and returns the ledger entries the store must
    /// persist in place of the old ones. <see cref="FinanceTransactionKind.Kind"/> may move among
    /// Income/Expense/Transfer only; converting to or from OpeningBalance or Distribution is
    /// refused, because those kinds carry structural guarantees an ordinary edit must not
    /// manufacture.
    /// </summary>
    public IReadOnlyList<FinanceLedgerEntry> Rewrite(
        FinanceTransactionEdit edit,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(edit);
        RequireEditableKind(Kind);
        RequireEditableKind(edit.Kind);

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }

        string description = RequiredBounded(
            edit.Description,
            MaximumDescriptionLength,
            nameof(edit.Description));
        string? reference = OptionalBounded(
            edit.Reference,
            MaximumReferenceLength,
            nameof(edit.Reference));
        string? counterpartyName = OptionalBounded(
            edit.CounterpartyName,
            MaximumCounterpartyNameLength,
            nameof(edit.CounterpartyName));
        actorEmail = RequiredBounded(actorEmail, MaximumActorEmailLength, nameof(actorEmail));

        List<FinanceLedgerEntry> entries;
        decimal amount = FinanceAmount.RequirePositive(edit.Amount, nameof(edit.Amount));
        FinanceCategory? category;

        switch (edit.Kind)
        {
            case FinanceTransactionKind.Income:
                if (edit.Category is null)
                {
                    throw new ArgumentException("An income category is required.", nameof(edit.Category));
                }

                RequireCategoryKind(edit.Category.Value, isIncome: true);
                category = edit.Category;
                entries =
                [
                    FinanceLedgerEntry.Create(
                        Id,
                        edit.AccountId,
                        edit.Kind,
                        FinanceLedgerLeg.Single,
                        amount,
                        edit.OccurredOn),
                ];
                break;

            case FinanceTransactionKind.Expense:
                if (edit.Category is null)
                {
                    throw new ArgumentException("An expense category is required.", nameof(edit.Category));
                }

                RequireCategoryKind(edit.Category.Value, isIncome: false);
                category = edit.Category;
                entries =
                [
                    FinanceLedgerEntry.Create(
                        Id,
                        edit.AccountId,
                        edit.Kind,
                        FinanceLedgerLeg.Single,
                        -amount,
                        edit.OccurredOn),
                ];
                break;

            case FinanceTransactionKind.Transfer:
                if (edit.ToAccountId is null)
                {
                    throw new ArgumentException(
                        "A destination account is required for a transfer.",
                        nameof(edit.ToAccountId));
                }

                if (edit.AccountId == edit.ToAccountId)
                {
                    throw new InvalidOperationException(
                        "A transfer cannot move money to the same account.");
                }

                category = null;
                entries =
                [
                    FinanceLedgerEntry.Create(
                        Id,
                        edit.AccountId,
                        edit.Kind,
                        FinanceLedgerLeg.From,
                        -amount,
                        edit.OccurredOn),
                    FinanceLedgerEntry.Create(
                        Id,
                        edit.ToAccountId.Value,
                        edit.Kind,
                        FinanceLedgerLeg.To,
                        amount,
                        edit.OccurredOn),
                ];
                break;

            default:
                throw new InvalidOperationException(
                    $"'{edit.Kind}' cannot be produced by an edit.");
        }

        Kind = edit.Kind;
        Category = category;
        Amount = amount;
        OccurredOn = edit.OccurredOn;
        Description = description;
        Reference = reference;
        CounterpartyName = counterpartyName;
        RevisionNumber += 1;
        UpdatedByUserId = actorUserId;
        UpdatedByEmail = actorEmail;
        UpdatedAtUtc = updatedAtUtc;

        return entries;
    }

    private static void RequireEditableKind(FinanceTransactionKind kind)
    {
        if (kind is FinanceTransactionKind.OpeningBalance or FinanceTransactionKind.Distribution)
        {
            throw new InvalidOperationException(
                $"'{kind}' cannot be produced or replaced by an ordinary edit.");
        }
    }

    private static void RequireCategoryKind(FinanceCategory category, bool isIncome)
    {
        bool valid = isIncome ? FinanceCategories.IsIncome(category) : FinanceCategories.IsExpense(category);
        if (!valid)
        {
            throw new ArgumentException(
                $"'{category}' is not a valid {(isIncome ? "income" : "expense")} category.",
                nameof(category));
        }
    }

    private static FinanceTransaction BuildNew(
        FinanceTransactionKind kind,
        FinanceCategory? category,
        decimal amount,
        DateOnly occurredOn,
        string description,
        string? reference,
        string? counterpartyName,
        Guid? financeDistributionId,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset nowUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }

        description = RequiredBounded(description, MaximumDescriptionLength, nameof(description));
        reference = OptionalBounded(reference, MaximumReferenceLength, nameof(reference));
        counterpartyName = OptionalBounded(
            counterpartyName,
            MaximumCounterpartyNameLength,
            nameof(counterpartyName));
        actorEmail = RequiredBounded(actorEmail, MaximumActorEmailLength, nameof(actorEmail));

        return new FinanceTransaction
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            Category = category,
            Amount = amount,
            OccurredOn = occurredOn,
            Description = description,
            Reference = reference,
            CounterpartyName = counterpartyName,
            FinanceDistributionId = financeDistributionId,
            RevisionNumber = 1,
            CreatedByUserId = actorUserId,
            CreatedByEmail = actorEmail,
            CreatedAtUtc = nowUtc,
            UpdatedByUserId = actorUserId,
            UpdatedByEmail = actorEmail,
            UpdatedAtUtc = nowUtc,
        };
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
