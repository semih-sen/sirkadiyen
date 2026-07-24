namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>Bounds and schedules the periodic Calendar/ledger inventory sweep.</summary>
public sealed class CalendarInventoryReconciliationOptions
{
    public int ConnectionBatchSize { get; init; } = 5;

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(24);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionBatchSize, 1);
        if (Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Interval),
                "The inventory interval must be positive.");
        }
    }
}
