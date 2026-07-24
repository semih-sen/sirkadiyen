namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Bounds how much initial-synchronization work one worker cycle does, so a large first load
/// is spread across cycles and stays within Calendar quota (ADR-058).
/// </summary>
public sealed class InitialSyncOptions
{
    /// <summary>How many users' initial syncs one cycle advances.</summary>
    public int ConnectionBatchSize { get; init; } = 5;

    /// <summary>How many events one cycle writes for a single user before deferring the rest.</summary>
    public int EventsPerConnectionPerCycle { get; init; } = 100;

    /// <summary>The summary (display name) given to each user's dedicated calendar (ADR-024).</summary>
    public string CalendarSummary { get; init; } = "Sirkadiyen";

    /// <summary>The IANA time zone the dedicated calendar is created in.</summary>
    public string CalendarTimeZoneId { get; init; } = "Europe/Istanbul";

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(EventsPerConnectionPerCycle, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(CalendarSummary);
        ArgumentException.ThrowIfNullOrWhiteSpace(CalendarTimeZoneId);
    }
}
