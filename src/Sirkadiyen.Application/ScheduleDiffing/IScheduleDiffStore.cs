using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Application.ScheduleDiffing;

/// <summary>
/// Loads the two revisions a diff compares and stores the result exactly once.
/// </summary>
public interface IScheduleDiffStore
{
    /// <summary>
    /// Loads the records needed to diff one published revision.
    /// </summary>
    /// <returns>
    /// The input, or <see langword="null"/> when the revision has never been
    /// published or already has a diff. Both are normal: publication and diff
    /// calculation are separate steps and either may be repeated after a crash.
    /// </returns>
    Task<ScheduleDiffInput?> LoadAsync(Guid revisionId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists revisions that reached publication but have no diff yet, oldest first.
    /// </summary>
    /// <remarks>
    /// A revision that was published and then superseded before its diff was
    /// calculated is still included. Skipping it would silently drop everything
    /// that revision changed.
    /// </remarks>
    Task<IReadOnlyList<Guid>> ListPendingDiffAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Stores a diff and its entries in one transaction.</summary>
    Task<ScheduleDiffPersistenceResult> SaveAsync(
        ScheduleDiff diff,
        CancellationToken cancellationToken);
}

public sealed record ScheduleDiffInput
{
    public required Guid ScheduleSourceId { get; init; }

    public required Domain.ScheduleSources.SourceId SourceId { get; init; }

    public required Guid CurrentRevisionId { get; init; }

    public Guid? PreviousRevisionId { get; init; }

    /// <summary>The superseded revision's records, empty for a first publication.</summary>
    public required IReadOnlyList<CanonicalScheduleRecord> PreviousRecords { get; init; }

    public required IReadOnlyList<CanonicalScheduleRecord> CurrentRecords { get; init; }
}

public sealed record ScheduleDiffPersistenceResult
{
    public required ScheduleDiffPersistenceOutcome Outcome { get; init; }

    /// <summary>The stored diff's identifier, or the existing one's when it was already stored.</summary>
    public Guid? ScheduleDiffId { get; init; }
}

public enum ScheduleDiffPersistenceOutcome
{
    Stored,

    /// <summary>Another pass stored a diff for this revision first.</summary>
    AlreadyCalculated,
}
