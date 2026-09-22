namespace Sirkadiyen.Domain.Scheduling.Diffing;

/// <summary>
/// How often a revision whose diff calculation failed is tried again, and when
/// the attempts stop (ADR-164).
/// </summary>
/// <remarks>
/// "Pending" is derived — a published revision with no diff row — so a failed
/// attempt leaves no trace of itself and the revision is due again on the very
/// next worker cycle. That is right for a crash mid-calculation and wrong for a
/// fault that will fail identically every time: for three weeks in September
/// 2026 four revisions were recalculated every six seconds because the diff they
/// produced could not be stored at all. These limits bound what a permanent
/// fault costs while leaving a transient one its recovery.
/// </remarks>
public sealed record ScheduleDiffRetryOptions
{
    /// <summary>How long to wait after the first failure; doubled on each one after it.</summary>
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many failures a revision may accumulate before automatic recalculation stops and an
    /// operator has to look at it.
    /// </summary>
    /// <remarks>
    /// Six attempts on a one-minute base is roughly half an hour of back-off before giving up —
    /// long enough to outlast a database restart or a deployment, short enough that a genuinely
    /// broken revision is in front of an operator the same morning.
    /// </remarks>
    public int MaximumAttempts { get; init; } = 6;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            BaseRetryDelay.Ticks,
            nameof(BaseRetryDelay));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaximumAttempts,
            nameof(MaximumAttempts));
    }
}
