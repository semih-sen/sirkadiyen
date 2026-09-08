namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// Bounds how hard this process may drive the Google Calendar API (ADR-157).
/// </summary>
/// <remarks>
/// The synchronization services decide how much work to do; this decides how much of it may be in
/// flight at once. It is a project-wide ceiling rather than a per-user one, because the per-user
/// rate limit is already spent by the student whose calendar is being written, while the project
/// quota is the single resource every concurrent write shares. Lowering it to one restores the
/// strictly serial behaviour, which is the kill switch when Google starts rejecting calls.
/// </remarks>
public sealed record GoogleCalendarThrottleOptions
{
    /// <summary>Google Calendar calls this process may have in flight at once; 0 removes the ceiling.</summary>
    public int MaxConcurrentCalls { get; init; } = 24;

    /// <summary>
    /// How many times one call retries a genuinely transient failure before giving up for this
    /// cycle. Higher than the serial default was: concurrency makes a rate-limit rejection an
    /// expected outcome to ride out rather than a signal that something is wrong.
    /// </summary>
    public int MaxTransientAttempts { get; init; } = 5;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxConcurrentCalls);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxTransientAttempts, 1);
    }
}
