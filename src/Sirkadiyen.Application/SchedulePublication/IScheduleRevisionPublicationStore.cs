using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Application.SchedulePublication;

/// <summary>
/// Makes a validated revision live, and releases a quarantined one so that it
/// can become live.
/// </summary>
/// <remarks>
/// Publication is the moment parsed data starts driving student calendars, so it
/// is deliberately the narrowest interface in the pipeline. It cannot validate,
/// it cannot parse, and it cannot reach a revision validation has not already
/// cleared or an administrator has not already approved.
/// </remarks>
public interface IScheduleRevisionPublicationStore
{
    Task<OperationalFreezeScope?> GetOperationalScopeAsync(
        Guid revisionId,
        CancellationToken cancellationToken) =>
        Task.FromResult<OperationalFreezeScope?>(null);

    /// <summary>
    /// Moves a <see cref="RevisionState.Validated"/> revision to
    /// <see cref="RevisionState.Published"/> and supersedes the source's
    /// previously published revision in the same transaction.
    /// </summary>
    Task<RevisionPublicationResult> PublishAsync(
        Guid revisionId,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>Lists validated revisions awaiting publication, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListPublishableAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records an approval against a quarantined revision and returns it to
    /// <see cref="RevisionState.Validated"/>.
    /// </summary>
    Task<RevisionApprovalResult> ApproveAsync(
        Guid revisionId,
        string approvedBy,
        string approvalReason,
        DateTimeOffset approvedAtUtc,
        CancellationToken cancellationToken);
}

public sealed record RevisionPublicationResult
{
    public required Guid RevisionId { get; init; }

    public required RevisionPublicationOutcome Outcome { get; init; }

    /// <summary>The revision this one replaced, when there was one.</summary>
    public Guid? SupersededRevisionId { get; init; }

    /// <summary>The state the revision was actually in, when it could not be published.</summary>
    public RevisionState? ObservedState { get; init; }
}

public enum RevisionPublicationOutcome
{
    Published,

    /// <summary>The global operational freeze prevented publication.</summary>
    Frozen,

    RevisionNotFound,

    /// <summary>The revision has not been validated, or is already live or terminal.</summary>
    NotValidated,

    /// <summary>
    /// A revision parsed more recently is already published for this source.
    /// </summary>
    /// <remarks>
    /// Publishing this one would move the source's live schedule backwards in
    /// time, which is indistinguishable from a mass deletion once the semantic
    /// diff runs over it.
    /// </remarks>
    SupersededByNewerRevision,

    /// <summary>Another publication for the same source committed first.</summary>
    ConcurrentPublication,
}

public sealed record RevisionApprovalResult
{
    public required Guid RevisionId { get; init; }

    public required RevisionApprovalOutcome Outcome { get; init; }

    public RevisionState? ObservedState { get; init; }
}

public enum RevisionApprovalOutcome
{
    Approved,

    RevisionNotFound,

    /// <summary>The revision is not waiting for review, so there is nothing to release.</summary>
    NotAwaitingReview,
}
