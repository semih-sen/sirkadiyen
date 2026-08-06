namespace Sirkadiyen.Domain.ScheduleDiffing;

/// <summary>
/// The limits above which a calculated diff is held instead of dispatched.
/// </summary>
/// <remarks>
/// Revision validation already applies a mass-deletion rule before publication,
/// but it compares stable-identity sets: it cannot know that a lesson which
/// changed time will be recovered by secondary matching, nor that an ambiguous
/// candidate set will refuse to resolve. This gate runs on the semantic result,
/// which is the number that actually decides how many calendar events would be
/// deleted, so both checks are needed and neither replaces the other.
/// </remarks>
public sealed record ScheduleDiffSafetyThresholds
{
    /// <summary>The share of the previous revision that may semantically disappear.</summary>
    public double MaximumDeletionShare { get; init; } = 0.20;

    /// <summary>
    /// How many deletions must occur before the share is consulted at all.
    /// </summary>
    /// <remarks>
    /// A source with four records would otherwise be held for a single ordinary
    /// deletion, which trains operators to approve without reading.
    /// </remarks>
    public int MinimumDeletionCount { get; init; } = 10;

    public void Validate()
    {
        if (MaximumDeletionShare is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumDeletionShare),
                MaximumDeletionShare,
                "The tolerated deletion share must be greater than 0 and at most 1.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MinimumDeletionCount,
            nameof(MinimumDeletionCount));
    }
}
