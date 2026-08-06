using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// Makes one revision live and retires the one it replaces, atomically.
/// </summary>
/// <remarks>
/// The writes run through <see cref="RetriableTransaction"/>, which is what lets
/// a store open its own transaction against a context configured to retry.
/// </remarks>
public sealed class ScheduleRevisionPublicationStore(SirkadiyenDbContext dbContext)
    : IScheduleRevisionPublicationStore
{
    /// <summary>PostgreSQL's unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public Task<OperationalFreezeScope?> GetOperationalScopeAsync(
        Guid revisionId,
        CancellationToken cancellationToken) =>
        (from revision in dbContext.ScheduleRevisions.AsNoTracking()
         join source in dbContext.ScheduleSources.AsNoTracking()
             on revision.ScheduleSourceId equals source.Id
         where revision.Id == revisionId
         select new OperationalFreezeScope
         {
             ClassYear = source.ClassYear,
             ProgramLanguage = source.ProgramLanguage,
         }).SingleOrDefaultAsync(cancellationToken);

    public Task<RevisionPublicationResult> PublishAsync(
        Guid revisionId,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            ScheduleRevision? revision = await dbContext.ScheduleRevisions
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == revisionId,
                    cancellationToken);

            if (revision is null)
            {
                return new RevisionPublicationResult
                {
                    RevisionId = revisionId,
                    Outcome = RevisionPublicationOutcome.RevisionNotFound,
                };
            }

            if (revision.State is not RevisionState.Validated)
            {
                // Includes the quarantined case. A held revision reaches
                // publication only by being approved first, which is a separate
                // call that leaves an audit trail behind it.
                return new RevisionPublicationResult
                {
                    RevisionId = revisionId,
                    Outcome = RevisionPublicationOutcome.NotValidated,
                    ObservedState = revision.State,
                };
            }

            ScheduleRevision? live = await dbContext.ScheduleRevisions
                .SingleOrDefaultAsync(
                    candidate => candidate.ScheduleSourceId == revision.ScheduleSourceId
                        && candidate.State == RevisionState.Published,
                    cancellationToken);

            if (live is not null && live.CreatedAtUtc >= revision.CreatedAtUtc)
            {
                return new RevisionPublicationResult
                {
                    RevisionId = revisionId,
                    Outcome = RevisionPublicationOutcome.SupersededByNewerRevision,
                    ObservedState = revision.State,
                    SupersededRevisionId = live.Id,
                };
            }

            try
            {
                if (live is not null)
                {
                    // Saved on its own, before the new revision is published. Only
                    // one revision per source may be Published, and that is a
                    // partial unique index rather than a deferrable constraint, so
                    // the old row has to vacate the slot in its own statement. Both
                    // writes are still inside one transaction.
                    live.MarkSuperseded(revision.Id, publishedAtUtc);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                revision.TransitionTo(
                    RevisionState.Published,
                    publishedAtUtc,
                    live is null
                        ? "Published as the first revision of this source."
                        : $"Published, superseding revision {live.Id}.");

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // Another publication for this source won the race. The loser
                // reports it rather than throwing: the winner's schedule is live
                // and correct, and this revision stays validated for a later pass.
                await transaction.RollbackAsync(cancellationToken);
                return new RevisionPublicationResult
                {
                    RevisionId = revisionId,
                    Outcome = RevisionPublicationOutcome.ConcurrentPublication,
                };
            }

            return new RevisionPublicationResult
            {
                RevisionId = revisionId,
                Outcome = RevisionPublicationOutcome.Published,
                SupersededRevisionId = live?.Id,
            };
        });

    public async Task<IReadOnlyList<Guid>> ListPublishableAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await dbContext.ScheduleRevisions
            .Where(revision => revision.State == RevisionState.Validated)
            .OrderBy(revision => revision.CreatedAtUtc)
            .Select(revision => revision.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<RevisionApprovalResult> ApproveAsync(
        Guid revisionId,
        string approvedBy,
        string approvalReason,
        DateTimeOffset approvedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalReason);

        return RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            ScheduleRevision? revision = await dbContext.ScheduleRevisions
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == revisionId,
                    cancellationToken);

            if (revision is null)
            {
                return new RevisionApprovalResult
                {
                    RevisionId = revisionId,
                    Outcome = RevisionApprovalOutcome.RevisionNotFound,
                };
            }

            if (revision.State is not RevisionState.ReviewRequired)
            {
                return new RevisionApprovalResult
                {
                    RevisionId = revisionId,
                    Outcome = RevisionApprovalOutcome.NotAwaitingReview,
                    ObservedState = revision.State,
                };
            }

            revision.Approve(approvedBy, approvalReason, approvedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new RevisionApprovalResult
            {
                RevisionId = revisionId,
                Outcome = RevisionApprovalOutcome.Approved,
            };
        });
    }

    public Task<RevisionRejectionResult> RejectAsync(
        Guid revisionId,
        string rejectedBy,
        string rejectionReason,
        DateTimeOffset rejectedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectionReason);

        return RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            ScheduleRevision? revision = await dbContext.ScheduleRevisions
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == revisionId,
                    cancellationToken);

            if (revision is null)
            {
                return new RevisionRejectionResult
                {
                    RevisionId = revisionId,
                    Outcome = RevisionRejectionOutcome.RevisionNotFound,
                };
            }

            if (revision.State is not RevisionState.ReviewRequired)
            {
                // Includes a revision another operator has already decided on. Rejecting anything
                // but a quarantined revision is refused rather than forced: publication is
                // corrected forward (ADR-033), never withdrawn.
                return new RevisionRejectionResult
                {
                    RevisionId = revisionId,
                    Outcome = RevisionRejectionOutcome.NotAwaitingReview,
                    ObservedState = revision.State,
                };
            }

            revision.Reject(rejectedBy, rejectionReason, rejectedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new RevisionRejectionResult
            {
                RevisionId = revisionId,
                Outcome = RevisionRejectionOutcome.Rejected,
            };
        });
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation };
}
