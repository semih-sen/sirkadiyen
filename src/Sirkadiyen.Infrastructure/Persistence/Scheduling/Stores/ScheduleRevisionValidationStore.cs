using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// Loads validation input and commits a validation outcome atomically.
/// </summary>
public sealed class ScheduleRevisionValidationStore(SirkadiyenDbContext dbContext)
    : IScheduleRevisionValidationStore
{
    public async Task<RevisionValidationInput?> LoadAsync(
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        ScheduleRevision? revision = await dbContext.ScheduleRevisions
            .SingleOrDefaultAsync(candidate => candidate.Id == revisionId, cancellationToken);

        if (revision is null || revision.State is not RevisionState.Parsed)
        {
            return null;
        }

        ScheduleSource source = await dbContext.ScheduleSources.SingleAsync(
            candidate => candidate.Id == revision.ScheduleSourceId,
            cancellationToken);

        List<CanonicalScheduleRecord> records = await dbContext.CanonicalScheduleRecords
            .Where(record => record.ScheduleRevisionId == revisionId)
            .OrderBy(record => record.CandidateId)
            .ToListAsync(cancellationToken);

        ScheduleRevision? published = await dbContext.ScheduleRevisions
            .SingleOrDefaultAsync(
                candidate => candidate.ScheduleSourceId == revision.ScheduleSourceId
                    && candidate.State == RevisionState.Published,
                cancellationToken);

        List<string> publishedIdentities = published is null
            ? []
            : await dbContext.CanonicalScheduleRecords
                .Where(record => record.ScheduleRevisionId == published.Id)
                .Select(record => record.StableIdentity)
                .ToListAsync(cancellationToken);

        return new RevisionValidationInput
        {
            Revision = revision,
            Source = source,
            Records = records,
            HasPreviousPublishedRevision = published is not null,
            PreviouslyPublishedIdentities = publishedIdentities,
        };
    }

    public Task ApplyAsync(
        Guid revisionId,
        RevisionValidationResult result,
        DateTimeOffset validatedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is RevisionState.Published)
        {
            throw new InvalidOperationException(
                "Validation may not publish a revision; publication is a separate step.");
        }

        return RetriableTransaction.ExecuteAsync(
            dbContext,
            () => ApplyCoreAsync(revisionId, result, validatedAtUtc, cancellationToken));
    }

    private async Task ApplyCoreAsync(
        Guid revisionId,
        RevisionValidationResult result,
        DateTimeOffset validatedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        ScheduleRevision revision = await dbContext.ScheduleRevisions.SingleAsync(
            candidate => candidate.Id == revisionId,
            cancellationToken);

        if (revision.State is not RevisionState.Parsed)
        {
            // Another cycle validated this revision first. Its findings are
            // already stored, so there is nothing to add and nothing to undo.
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        revision.TransitionTo(RevisionState.Validating, validatedAtUtc);
        revision.TransitionTo(result.Outcome, validatedAtUtc, result.StateReason);
        dbContext.RevisionValidationFindings.AddRange(result.Findings);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListPendingValidationAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await dbContext.ScheduleRevisions
            .Where(revision => revision.State == RevisionState.Parsed)
            .OrderBy(revision => revision.CreatedAtUtc)
            .Select(revision => revision.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
