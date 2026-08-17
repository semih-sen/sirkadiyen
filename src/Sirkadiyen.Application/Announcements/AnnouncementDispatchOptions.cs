namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// Bounds how much announcement delivery one worker cycle does, so a campaign addressed to a whole
/// class spreads across cycles and stays inside Calendar quota (ADR-065's rule, ADR-107).
/// </summary>
public sealed class AnnouncementDispatchOptions
{
    /// <summary>How many announcements one cycle works on.</summary>
    public int AnnouncementBatchSize { get; init; } = 3;

    /// <summary>
    /// How many Calendar mutations one cycle performs for a single announcement before yielding.
    /// Reaching it leaves the announcement mid-delivery; that is an ordinary yield, not a failure,
    /// and it carries no back-off.
    /// </summary>
    public int CalendarOperationsPerAnnouncementPerCycle { get; init; } = 200;

    /// <summary>
    /// How many delivery passes may fail transiently before an operator has to look at it. Without
    /// a cap, a permanently broken announcement retries every cycle forever and hides itself among
    /// the healthy ones.
    /// </summary>
    public int MaximumDeliveryAttempts { get; init; } = 8;

    /// <summary>The first retry delay; it doubles per attempt up to <see cref="MaximumBackoff"/>.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan MaximumBackoff { get; init; } = TimeSpan.FromHours(2);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(AnnouncementBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(CalendarOperationsPerAnnouncementPerCycle, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumDeliveryAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(InitialBackoff, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumBackoff, InitialBackoff);
    }

    /// <summary>The delay before the next attempt, doubling and then flattening at the cap.</summary>
    public TimeSpan BackoffFor(int attempts)
    {
        if (attempts <= 1)
        {
            return InitialBackoff;
        }

        double multiplier = Math.Pow(2, Math.Min(attempts - 1, 16));
        double ticks = InitialBackoff.Ticks * multiplier;
        return ticks >= MaximumBackoff.Ticks
            ? MaximumBackoff
            : TimeSpan.FromTicks((long)ticks);
    }
}
