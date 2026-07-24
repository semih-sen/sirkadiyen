namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Bounds the semantic-diff replay performed for re-authorized Calendar connections
/// in one worker cycle (ADR-060).
/// </summary>
public sealed class CalendarReconciliationOptions
{
    /// <summary>How many re-authorized connections one cycle advances.</summary>
    public int ConnectionBatchSize { get; init; } = 5;

    /// <summary>
    /// How many already-dispatched diffs one connection replays before yielding to a later cycle.
    /// The cursor advances after each complete diff, so yielding never splits its authority.
    /// </summary>
    public int DiffsPerConnectionPerCycle { get; init; } = 10;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DiffsPerConnectionPerCycle, 1);
    }
}
