using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Reads the two revisions a diff compares and stores the result once.
/// </summary>
/// <remarks>
/// A diff and its entries are written by a single <c>SaveChanges</c>, which is
/// already one transaction, so this store does not open its own and does not
/// need <see cref="RetriableTransaction"/>.
/// </remarks>
public sealed class ScheduleDiffStore(SirkadiyenDbContext dbContext) : IScheduleDiffStore
{
    /// <summary>PostgreSQL's unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public async Task<ScheduleDiffInput?> LoadAsync(
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        ScheduleRevision? revision = await dbContext.ScheduleRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == revisionId, cancellationToken);

        // Superseded counts as published: a revision that went live and was
        // replaced before its diff ran still changed the schedule, and skipping
        // it would lose everything it changed.
        if (revision is null
            || revision.State is not (RevisionState.Published or RevisionState.Superseded))
        {
            return null;
        }

        bool alreadyCalculated = await dbContext.ScheduleDiffs
            .AnyAsync(diff => diff.CurrentRevisionId == revisionId, cancellationToken);
        if (alreadyCalculated)
        {
            return null;
        }

        // The revision this one replaced states so itself, which is more
        // reliable than re-deriving it from creation order: publication wrote it
        // inside the same transaction that made this revision live.
        ScheduleRevision? previous = await dbContext.ScheduleRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.SupersededByRevisionId == revisionId,
                cancellationToken);

        return new ScheduleDiffInput
        {
            ScheduleSourceId = revision.ScheduleSourceId,
            SourceId = revision.SourceId,
            CurrentRevisionId = revision.Id,
            PreviousRevisionId = previous?.Id,
            PreviousRecords = previous is null
                ? []
                : await LoadRecordsAsync(previous.Id, cancellationToken),
            CurrentRecords = await LoadRecordsAsync(revision.Id, cancellationToken),
        };
    }

    public async Task<IReadOnlyList<Guid>> ListPendingDiffAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await dbContext.ScheduleRevisions
            .Where(revision =>
                (revision.State == RevisionState.Published
                    || revision.State == RevisionState.Superseded)
                && !dbContext.ScheduleDiffs.Any(diff => diff.CurrentRevisionId == revision.Id))
            .OrderBy(revision => revision.PublishedAtUtc)
            .ThenBy(revision => revision.Id)
            .Select(revision => revision.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<ScheduleDiffPersistenceResult> SaveAsync(
        ScheduleDiff diff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diff);

        dbContext.ScheduleDiffs.Add(diff);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Another pass diffed this revision first. Both passes read the same
            // two immutable revisions, so the stored diff says the same thing;
            // reporting it beats writing a second set of calendar operations.
            dbContext.ChangeTracker.Clear();
            return new ScheduleDiffPersistenceResult
            {
                Outcome = ScheduleDiffPersistenceOutcome.AlreadyCalculated,
                ScheduleDiffId = await dbContext.ScheduleDiffs
                    .Where(candidate => candidate.CurrentRevisionId == diff.CurrentRevisionId)
                    .Select(candidate => (Guid?)candidate.Id)
                    .SingleOrDefaultAsync(cancellationToken),
            };
        }

        return new ScheduleDiffPersistenceResult
        {
            Outcome = ScheduleDiffPersistenceOutcome.Stored,
            ScheduleDiffId = diff.Id,
        };
    }

    private async Task<IReadOnlyList<CanonicalScheduleRecord>> LoadRecordsAsync(
        Guid revisionId,
        CancellationToken cancellationToken) =>
        await dbContext.CanonicalScheduleRecords
            .AsNoTracking()
            .Where(record => record.ScheduleRevisionId == revisionId)
            .OrderBy(record => record.CandidateId)
            .ToListAsync(cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation };
}
