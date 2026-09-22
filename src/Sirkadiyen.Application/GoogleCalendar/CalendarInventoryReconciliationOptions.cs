namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>Bounds and schedules the periodic Calendar/ledger inventory sweep.</summary>
public sealed class CalendarInventoryReconciliationOptions
{
    public int ConnectionBatchSize { get; init; } = 5;

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How many Calendar writes one sweep may perform before yielding the shared Calendar
    /// fence and resuming on a catch-up cycle.
    /// </summary>
    /// <remarks>
    /// Every other Calendar stage has had such a budget from the start; inventory did not,
    /// which is what let a comparison defect turn one sweep into a rewrite of thousands of
    /// events. A sweep that finds nothing to repair — the normal case — issues no writes at
    /// all and is therefore never slowed by this. What it bounds is the damage a sweep can do
    /// while holding the fence: initial synchronization, dispatch and replay keep getting
    /// their turn every catch-up interval rather than waiting out a full rewrite.
    /// </remarks>
    public int CalendarOperationsPerRun { get; init; } = 300;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(CalendarOperationsPerRun, 1);
        if (Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Interval),
                "The inventory interval must be positive.");
        }
    }
}
