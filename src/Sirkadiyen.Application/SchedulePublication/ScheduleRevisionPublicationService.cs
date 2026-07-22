namespace Sirkadiyen.Application.SchedulePublication;

/// <summary>
/// Publishes validated revisions, and approves quarantined ones on an
/// administrator's behalf (ADR-032).
/// </summary>
/// <remarks>
/// A revision that passes every validation rule is published without anyone
/// being asked: holding a healthy schedule back helps nobody, and the safety
/// nets that matter already ran in validation. Human judgement is spent only on
/// the revisions validation refused to clear.
/// <para>
/// Publication is driven by revision state rather than by whoever created the
/// revision, so a cycle that crashed between validation and publication is
/// simply picked up by <see cref="PublishPendingAsync"/> on the next pass.
/// </para>
/// </remarks>
public sealed class ScheduleRevisionPublicationService(
    IScheduleRevisionPublicationStore store,
    TimeProvider timeProvider)
{
    /// <summary>Publishes one revision, if it is in a state that allows it.</summary>
    public Task<RevisionPublicationResult> PublishAsync(
        Guid revisionId,
        CancellationToken cancellationToken) =>
        store.PublishAsync(revisionId, timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>
    /// Publishes every validated revision waiting for publication, oldest first.
    /// </summary>
    /// <remarks>
    /// A revision that cannot be published is reported rather than thrown, so
    /// one stale revision cannot stop the rest of the queue from going live.
    /// </remarks>
    public async Task<IReadOnlyList<RevisionPublicationResult>> PublishPendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<Guid> publishable = await store.ListPublishableAsync(
            limit,
            cancellationToken);

        List<RevisionPublicationResult> results = [];
        foreach (Guid revisionId in publishable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await store.PublishAsync(
                revisionId,
                timeProvider.GetUtcNow(),
                cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// Approves a quarantined revision and, when that succeeds, publishes it.
    /// </summary>
    /// <remarks>
    /// Approval and publication stay two transactions. An approval that commits
    /// and a publication that then fails leaves the revision validated and in
    /// the publication queue, which the next pass drains; the alternative would
    /// be losing the record of who approved it.
    /// </remarks>
    public async Task<RevisionApprovalOutcomeResult> ApproveAndPublishAsync(
        Guid revisionId,
        string approvedBy,
        string approvalReason,
        CancellationToken cancellationToken)
    {
        RevisionApprovalResult approval = await store.ApproveAsync(
            revisionId,
            approvedBy,
            approvalReason,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (approval.Outcome is not RevisionApprovalOutcome.Approved)
        {
            return new RevisionApprovalOutcomeResult { Approval = approval };
        }

        return new RevisionApprovalOutcomeResult
        {
            Approval = approval,
            Publication = await store.PublishAsync(
                revisionId,
                timeProvider.GetUtcNow(),
                cancellationToken),
        };
    }
}

public sealed record RevisionApprovalOutcomeResult
{
    public required RevisionApprovalResult Approval { get; init; }

    /// <summary>The publication attempt, or <see langword="null"/> if approval failed.</summary>
    public RevisionPublicationResult? Publication { get; init; }
}
