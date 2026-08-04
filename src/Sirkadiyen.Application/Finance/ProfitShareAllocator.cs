using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

/// <summary>One partner's basis-point share, as input to <see cref="ProfitShareAllocator"/>.</summary>
public sealed record ProfitShareInput
{
    public required Guid HolderId { get; init; }

    public required int ShareBasisPoints { get; init; }
}

/// <summary>One partner's allocated payout, as produced by <see cref="ProfitShareAllocator"/>.</summary>
public sealed record ProfitShareAllocation
{
    public required Guid HolderId { get; init; }

    public required int ShareBasisPoints { get; init; }

    /// <summary>The pre-rounding numerator in minor units (kuruş), kept for auditability.</summary>
    public required long ExactShareMinorUnits { get; init; }

    public required decimal AllocatedAmount { get; init; }

    public required bool RemainderUnitAwarded { get; init; }
}

/// <summary>
/// Splits a distributable amount across partners using largest-remainder allocation in integer
/// minor units, so the sum of allocations always equals the input exactly (ADR-092 §7). Pure: no
/// I/O, no clock.
/// </summary>
public static class ProfitShareAllocator
{
    private const long BasisPointsDenominator = 10_000;

    public static IReadOnlyList<ProfitShareAllocation> Allocate(
        decimal distributableAmount,
        IReadOnlyList<ProfitShareInput> partners)
    {
        ArgumentNullException.ThrowIfNull(partners);
        if (partners.Count == 0)
        {
            throw new ArgumentException("At least one partner is required.", nameof(partners));
        }

        if (partners.Select(partner => partner.HolderId).Distinct().Count() != partners.Count)
        {
            throw new ArgumentException("Each partner must appear at most once.", nameof(partners));
        }

        foreach (ProfitShareInput partner in partners)
        {
            if (partner.ShareBasisPoints is < 1 or > FinanceAccountHolder.MaximumShareBasisPoints)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(partners),
                    partner.ShareBasisPoints,
                    $"Holder {partner.HolderId} has a share outside 1..{FinanceAccountHolder.MaximumShareBasisPoints}.");
            }
        }

        decimal validatedAmount = FinanceAmount.RequirePositive(
            distributableAmount,
            nameof(distributableAmount));

        long totalMinor = (long)(validatedAmount * 100m);
        if (totalMinor > long.MaxValue / BasisPointsDenominator)
        {
            throw new OverflowException(
                "The distributable amount is too large to allocate without overflow.");
        }

        List<Working> working = [.. partners.Select(partner =>
        {
            long exact = totalMinor * partner.ShareBasisPoints;
            return new Working(partner.HolderId, partner.ShareBasisPoints, exact, exact / BasisPointsDenominator, exact % BasisPointsDenominator);
        })];

        long allocatedBase = working.Sum(item => item.BaseUnits);
        long leftover = totalMinor - allocatedBase;

        if (leftover < 0 || leftover >= working.Count)
        {
            throw new InvalidOperationException(
                $"The largest-remainder allocation produced an out-of-range leftover of {leftover} " +
                $"units for {working.Count} partner(s); this indicates an arithmetic bug.");
        }

        HashSet<Guid> bonusHolderIds = [.. working
            .OrderByDescending(item => item.Remainder)
            .ThenByDescending(item => item.ShareBasisPoints)
            .ThenBy(item => item.HolderId, ChronologicalGuidComparer.Instance)
            .Take((int)leftover)
            .Select(item => item.HolderId)];

        List<ProfitShareAllocation> results = [.. working.Select(item =>
        {
            bool bonus = bonusHolderIds.Contains(item.HolderId);
            long minorUnits = item.BaseUnits + (bonus ? 1 : 0);
            return new ProfitShareAllocation
            {
                HolderId = item.HolderId,
                ShareBasisPoints = item.ShareBasisPoints,
                ExactShareMinorUnits = item.ExactMinorUnits,
                AllocatedAmount = minorUnits / 100m,
                RemainderUnitAwarded = bonus,
            };
        })];

        decimal sum = results.Sum(result => result.AllocatedAmount);
        if (sum != validatedAmount)
        {
            throw new InvalidOperationException(
                "The allocation does not sum to the distributable amount; this indicates an arithmetic bug.");
        }

        return results;
    }

    private sealed record Working(
        Guid HolderId,
        int ShareBasisPoints,
        long ExactMinorUnits,
        long BaseUnits,
        long Remainder);

    /// <summary>
    /// Orders <c>Guid.CreateVersion7()</c> identifiers by creation time. The default
    /// <see cref="Guid"/> comparison does not do this on little-endian platforms, because it
    /// compares the mixed-endian in-memory field layout rather than the RFC byte order; comparing
    /// the big-endian byte representation instead matches both chronological creation order and
    /// PostgreSQL's native <c>uuid</c> ordering.
    /// </summary>
    private sealed class ChronologicalGuidComparer : IComparer<Guid>
    {
        public static readonly ChronologicalGuidComparer Instance = new();

        public int Compare(Guid left, Guid right)
        {
            Span<byte> leftBytes = stackalloc byte[16];
            Span<byte> rightBytes = stackalloc byte[16];
            left.TryWriteBytes(leftBytes, bigEndian: true, out _);
            right.TryWriteBytes(rightBytes, bigEndian: true, out _);
            return leftBytes.SequenceCompareTo(rightBytes);
        }
    }
}
