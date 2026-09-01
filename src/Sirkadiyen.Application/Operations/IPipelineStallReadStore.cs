namespace Sirkadiyen.Application.Operations;

/// <summary>
/// Reads how long the pipeline's waiting work has been waiting.
/// </summary>
/// <remarks>
/// Every method takes the cutoff rather than a duration, so the clock belongs to
/// the caller and the store stays a pure read over state that already exists.
/// Nothing here writes, and nothing here is on a request path.
/// </remarks>
public interface IPipelineStallReadStore
{
    /// <summary>Revisions quarantined for review since before the cutoff.</summary>
    Task<StalledWork> CountRevisionsAwaitingReviewAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revisions created before the cutoff that validation has still not run on.
    /// </summary>
    Task<StalledWork> CountRevisionsStuckBeforeValidationAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);

    /// <summary>Diffs held for an operator since before the cutoff.</summary>
    Task<StalledWork> CountDiffsAwaitingReleaseAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Diffs whose calendar dispatch gave up and now needs a named operator to
    /// retry it (ADR-097). Reported without an age: a dispatch that has failed
    /// terminally is already late.
    /// </summary>
    Task<StalledWork> CountFailedDispatchesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sources whose most recent snapshot was acquired before the cutoff, or that
    /// have never been acquired at all.
    /// </summary>
    Task<StalledWork> CountSourcesNotPolledSinceAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);
}
