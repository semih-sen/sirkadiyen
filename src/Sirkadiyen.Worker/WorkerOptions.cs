namespace Sirkadiyen.Worker;

internal sealed record WorkerOptions
{
    public required string SourceCatalogPath { get; init; }

    /// <summary>
    /// Delay before resuming ordinary quota-yielded Calendar work. This is deliberately
    /// independent from source polling: a large diff should drain promptly even at night or on a
    /// weekend without repeatedly downloading every source.
    /// </summary>
    public TimeSpan CalendarCatchUpInterval { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceCatalogPath);
        if (CalendarCatchUpInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The Calendar catch-up interval must be positive.");
        }
    }
}
