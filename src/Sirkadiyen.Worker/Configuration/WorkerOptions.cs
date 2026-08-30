namespace Sirkadiyen.Worker.Configuration;

internal sealed record WorkerOptions
{
    public required string SourceCatalogPath { get; init; }

    /// <summary>
    /// The catalog this release shipped, inside the deployed artifact (ADR-138).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SourceCatalogPath"/>, which is the running document in shared
    /// configuration. When the two resolve to the same file — every development run — nothing is
    /// installed. Null means this deployment ships no catalog.
    /// </remarks>
    public string? ShippedSourceCatalogPath { get; init; }

    /// <summary>The release this worker was deployed as, recorded on the catalog revision it installs.</summary>
    public string Release { get; init; } = "unknown release";

    /// <summary>
    /// Delay before resuming ordinary quota-yielded Calendar work. This is deliberately
    /// independent from source polling: a large diff should drain promptly even at night or on a
    /// weekend without repeatedly downloading every source.
    /// </summary>
    public TimeSpan CalendarCatchUpInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time an idle worker waits before checking for Calendar work that was queued
    /// after its previous pass. Source polling keeps its own adaptive deadline and is not
    /// repeated on these idle checks.
    /// </summary>
    public TimeSpan CalendarIdleCheckInterval { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceCatalogPath);
        if (CalendarCatchUpInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The Calendar catch-up interval must be positive.");
        }

        if (CalendarIdleCheckInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The Calendar idle-check interval must be positive.");
        }
    }
}
