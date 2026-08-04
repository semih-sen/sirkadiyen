using Sirkadiyen.Application.Operations;

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
    IOperationalFreezeStore operationalFreeze,
    TimeProvider timeProvider)
{
    /// <summary>Publishes one revision, if it is in a state that allows it.</summary>
    public async Task<RevisionPublicationResult> PublishAsync(
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        if ((await operationalFreeze.GetAsync(cancellationToken)).IsFrozen)
        {
            return Frozen(revisionId);
        }

        OperationalFreezeScope? scope = await store.GetOperationalScopeAsync(
            revisionId,
            cancellationToken);
        if (scope is not null
            && (await operationalFreeze.GetScopedAsync(scope, cancellationToken)).IsFrozen)
        {
            return Frozen(revisionId);
        }

        return await store.PublishAsync(
            revisionId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

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

        if (publishable.Count > 0
            && (await operationalFreeze.GetAsync(cancellationToken)).IsFrozen)
        {
            return [Frozen(publishable[0])];
        }

        List<RevisionPublicationResult> results = [];
        foreach (Guid revisionId in publishable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevisionPublicationResult result = await PublishAsync(
                revisionId,
                cancellationToken);
            results.Add(result);

            // A scoped freeze blocks only this revision's class/program pipeline;
            // later revisions may belong to another active scope and must continue.
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
            Publication = await PublishAsync(revisionId, cancellationToken),
        };
    }

    private static RevisionPublicationResult Frozen(Guid revisionId) => new()
    {
        RevisionId = revisionId,
        Outcome = RevisionPublicationOutcome.Frozen,
    };
}

public sealed record RevisionApprovalOutcomeResult
{
    public required RevisionApprovalResult Approval { get; init; }

    /// <summary>The publication attempt, or <see langword="null"/> if approval failed.</summary>
    public RevisionPublicationResult? Publication { get; init; }
}
