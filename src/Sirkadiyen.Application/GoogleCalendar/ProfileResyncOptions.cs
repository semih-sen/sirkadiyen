namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Bounds how much profile re-synchronization work one worker cycle does, so a cohort change on a
/// full calendar is spread across cycles and stays within Calendar quota (ADR-096, ADR-065).
/// </summary>
public sealed class ProfileResyncOptions
{
    /// <summary>How many users' calendars one cycle converges onto their changed profile.</summary>
    public int ConnectionBatchSize { get; init; } = 5;

    /// <summary>
    /// How many Calendar mutations one cycle performs for a single user before deferring the rest.
    /// A pass that reaches this leaves the request pending; it is not a failure and carries no
    /// back-off.
    /// </summary>
    public int CalendarOperationsPerConnectionPerCycle { get; init; } = 100;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(CalendarOperationsPerConnectionPerCycle, 1);
    }
}
