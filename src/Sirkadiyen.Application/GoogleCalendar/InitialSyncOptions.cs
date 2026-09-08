namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Bounds how much initial-synchronization work one worker cycle does, so a large first load
/// is spread across cycles and stays within Calendar quota (ADR-058).
/// </summary>
public sealed class InitialSyncOptions
{
    /// <summary>
    /// How many users' initial syncs one cycle advances. Kept equal to
    /// <see cref="UserConcurrency"/>: this is the pool the cycle draws from, so a smaller number
    /// here silently caps the concurrency below what that one asks for.
    /// </summary>
    public int ConnectionBatchSize { get; init; } = 8;

    /// <summary>How many events one cycle writes for a single user before deferring the rest.</summary>
    public int EventsPerConnectionPerCycle { get; init; } = 100;

    /// <summary>
    /// How many pending connections the worker advances at the same time, each in its own scope
    /// (ADR-157). One restores the strictly sequential pass this replaced.
    /// </summary>
    /// <remarks>
    /// Concurrency across *distinct* users is what makes a launch-day queue drain: Google's rate
    /// limit is per end user, so separate students barely contend. It does not reopen ADR-122's
    /// race, which was two workers on the *same* user; each connection is still advanced once, by
    /// one task, inside the one worker holding the calendar fence.
    /// </remarks>
    public int UserConcurrency { get; init; } = 8;

    /// <summary>
    /// How many events of a single user's calendar are written at the same time. Kept well below
    /// <see cref="UserConcurrency"/>: these all contend for one end user's Calendar rate limit.
    /// </summary>
    /// <remarks>
    /// Three is measured, not guessed. The project's Calendar quota allows 600 queries per minute
    /// per end user, and an event insert takes ~0.34s, so a degree of N sustains roughly N × 177
    /// queries per minute for that student: three fits with room to spare and four would sit above
    /// the limit, spending the pass on rate-limit rejections and back-off. Widening the load means
    /// raising <see cref="UserConcurrency"/> instead — the 10,000/minute project quota is the one
    /// with headroom.
    /// </remarks>
    public int EventWriteConcurrency { get; init; } = 3;

    /// <summary>The summary (display name) given to each user's dedicated calendar (ADR-024).</summary>
    public string CalendarSummary { get; init; } = "Sirkadiyen";

    /// <summary>The IANA time zone the dedicated calendar is created in.</summary>
    public string CalendarTimeZoneId { get; init; } = "Europe/Istanbul";

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(EventsPerConnectionPerCycle, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(UserConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(EventWriteConcurrency, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(CalendarSummary);
        ArgumentException.ThrowIfNullOrWhiteSpace(CalendarTimeZoneId);
    }
}
