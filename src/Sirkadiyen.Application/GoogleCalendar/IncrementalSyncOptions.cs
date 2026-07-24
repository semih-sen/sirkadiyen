namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Bounds how much incremental-dispatch work one worker cycle does and how it backs off after a
/// transient failure (ADR-059).
/// </summary>
public sealed class IncrementalSyncOptions
{
    /// <summary>How many dispatchable diffs one cycle fans out onto calendars.</summary>
    public int DiffDispatchBatchSize { get; init; } = 10;

    /// <summary>
    /// How many transient failures a diff tolerates before it is marked failed and left for an
    /// operator rather than retried every cycle.
    /// </summary>
    public int MaxDispatchAttempts { get; init; } = 5;

    /// <summary>
    /// The base of the exponential back-off between dispatch retries; the nth failure waits roughly
    /// this many seconds times two to the (n-1).
    /// </summary>
    public int DispatchRetryBaseDelaySeconds { get; init; } = 30;

    /// <summary>The base of the exponential back-off, as the domain expects it.</summary>
    public TimeSpan RetryBaseDelay => TimeSpan.FromSeconds(DispatchRetryBaseDelaySeconds);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(DiffDispatchBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxDispatchAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DispatchRetryBaseDelaySeconds, 1);
    }
}
