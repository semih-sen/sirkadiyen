namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Controls how long non-anchor normalized snapshot documents remain online.
/// </summary>
public sealed record SnapshotRetentionOptions
{
    public TimeSpan RecentWindow { get; init; } = TimeSpan.FromDays(10);

    public int BatchSize { get; init; } = 50;

    public void Validate()
    {
        if (RecentWindow <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The snapshot retention window must be greater than zero.");
        }

        if (BatchSize is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "The snapshot retention batch size must be between 1 and 1000.");
        }
    }
}
