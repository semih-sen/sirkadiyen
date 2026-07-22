using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Application.SchedulePublication;

/// <summary>
/// Loads what revision validation needs and records what it decided.
/// </summary>
/// <remarks>
/// Validation is deliberately a separate transaction from parse completion. A
/// revision left in <see cref="RevisionState.Parsed"/> by an interrupted worker
/// stays valid input for a later pass, so validation can always be retried
/// without re-parsing an immutable snapshot.
/// </remarks>
public interface IScheduleRevisionValidationStore
{
    /// <summary>
    /// Loads a candidate revision, its records, its source, and the stable
    /// identities of the last published revision for that source.
    /// </summary>
    /// <returns>
    /// The gathered input, or <see langword="null"/> when the revision is no
    /// longer in a state that may be validated.
    /// </returns>
    Task<RevisionValidationInput?> LoadAsync(
        Guid revisionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Transitions the revision and stores its findings in one transaction.
    /// </summary>
    Task ApplyAsync(
        Guid revisionId,
        RevisionValidationResult result,
        DateTimeOffset validatedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>Lists revisions still awaiting validation, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListPendingValidationAsync(
        int limit,
        CancellationToken cancellationToken);
}
