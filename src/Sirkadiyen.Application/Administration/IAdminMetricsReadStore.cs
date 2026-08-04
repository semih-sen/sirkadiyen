namespace Sirkadiyen.Application.Administration;

/// <summary>
/// A point-in-time aggregate of the operational signals the admin server dashboard shows
/// (AI_GUIDELINE §19). Every value is a real count read from an existing table; nothing is
/// sampled or synthesized.
/// </summary>
public interface IAdminMetricsReadStore
{
    Task<AdminMetricsSnapshot> GetAsync(CancellationToken cancellationToken);
}

public sealed record AdminMetricsSnapshot
{
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public required int TotalUsers { get; init; }

    public required int ActiveLicenses { get; init; }

    /// <summary>Connections whose one-time initial synchronization is still running.</summary>
    public required int InitialSyncsInProgress { get; init; }

    public required int CompletedConnections { get; init; }

    /// <summary>Revisions quarantined for operator review — part of the pipeline queue depth.</summary>
    public required int RevisionsAwaitingReview { get; init; }

    /// <summary>Diffs held from dispatch — the rest of the pipeline queue depth.</summary>
    public required int HeldDiffs { get; init; }

    /// <summary>Polling-enabled sources not successfully polled within the overdue window.</summary>
    public required int PollingSourcesOverdue { get; init; }

    public required bool OperationalFreezeActive { get; init; }
}
