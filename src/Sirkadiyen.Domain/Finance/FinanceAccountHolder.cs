namespace Sirkadiyen.Domain.Finance;

/// <summary>
/// Whose cash box or bank account a <see cref="FinanceAccount"/> belongs to. This is not an
/// authorization role (ADR-092 §9) — every finance endpoint still requires the SuperAdmin policy.
/// A holder with a nonzero <see cref="ShareBasisPoints"/> is a profit-distribution partner.
/// </summary>
public sealed class FinanceAccountHolder
{
    public const int MaximumDisplayNameLength = 200;

    public const int MinimumShareBasisPoints = 0;

    public const int MaximumShareBasisPoints = 10_000;

    private FinanceAccountHolder()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public string DisplayName { get; private set; } = string.Empty;

    public Guid? UserId { get; private init; }

    /// <summary><c>0</c> means this holder is not a profit-distribution partner.</summary>
    public int ShareBasisPoints { get; private set; }

    public FinanceAccountHolderStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsEligiblePartner => Status == FinanceAccountHolderStatus.Active && ShareBasisPoints > 0;

    public static FinanceAccountHolder Create(
        string displayName,
        Guid? userId,
        int shareBasisPoints,
        DateTimeOffset createdAtUtc)
    {
        displayName = RequiredBounded(displayName, MaximumDisplayNameLength, nameof(displayName));
        RequireValidShare(shareBasisPoints, nameof(shareBasisPoints));

        return new FinanceAccountHolder
        {
            Id = Guid.CreateVersion7(),
            DisplayName = displayName,
            UserId = userId,
            ShareBasisPoints = shareBasisPoints,
            Status = FinanceAccountHolderStatus.Active,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    public void Rename(string displayName, DateTimeOffset updatedAtUtc)
    {
        DisplayName = RequiredBounded(displayName, MaximumDisplayNameLength, nameof(displayName));
        UpdatedAtUtc = updatedAtUtc;
    }

    public void SetShare(int shareBasisPoints, DateTimeOffset updatedAtUtc)
    {
        if (Status != FinanceAccountHolderStatus.Active)
        {
            throw new InvalidOperationException(
                "An inactive holder cannot be given a share. Reactivate the holder first.");
        }

        RequireValidShare(shareBasisPoints, nameof(shareBasisPoints));
        ShareBasisPoints = shareBasisPoints;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Deactivate(DateTimeOffset updatedAtUtc)
    {
        if (Status == FinanceAccountHolderStatus.Inactive)
        {
            throw new InvalidOperationException("The holder is already inactive.");
        }

        Status = FinanceAccountHolderStatus.Inactive;
        ShareBasisPoints = 0;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static void RequireValidShare(int shareBasisPoints, string parameterName)
    {
        if (shareBasisPoints < MinimumShareBasisPoints || shareBasisPoints > MaximumShareBasisPoints)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                shareBasisPoints,
                $"'{parameterName}' must be between {MinimumShareBasisPoints} and {MaximumShareBasisPoints}.");
        }
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}

public enum FinanceAccountHolderStatus
{
    Active,
    Inactive,
}
