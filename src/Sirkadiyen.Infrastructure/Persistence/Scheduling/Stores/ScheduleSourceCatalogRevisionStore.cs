using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// The append-only catalog history, and the single transaction that makes an edit take effect
/// (ADR-114).
/// </summary>
public sealed class ScheduleSourceCatalogRevisionStore(SirkadiyenDbContext dbContext)
    : IScheduleSourceCatalogRevisionStore
{
    /// <summary>
    /// Records the revision, applies its sources and retires the ones it dropped, atomically.
    /// </summary>
    /// <remarks>
    /// One transaction, because a revision that did not apply and an application no revision
    /// explains are both states in which nobody can say what the system is running. The baseline
    /// is inserted here rather than by the caller so the "is the history empty" test and the
    /// insert cannot interleave with a second administrator's first edit.
    /// </remarks>
    public async Task<int> CommitAsync(
        ScheduleSourceCatalogCommit commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);

        return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (commit.Baseline is { } baseline
                && !await dbContext.ScheduleSourceCatalogRevisions.AnyAsync(cancellationToken))
            {
                dbContext.ScheduleSourceCatalogRevisions.Add(
                    ScheduleSourceCatalogRevision.Baseline(
                        baseline.RecordedAtUtc,
                        baseline.Content,
                        baseline.ContentHash,
                        baseline.SourceCount));
            }

            dbContext.ScheduleSourceCatalogRevisions.Add(commit.Revision);

            int changed = await ScheduleSourceUpsert.StageAsync(
                dbContext,
                commit.Sources,
                cancellationToken);

            if (commit.PollingDisabled.Count > 0)
            {
                // Polling off, nothing deleted. A source dropped from the document keeps its row,
                // its snapshots, its revisions and every calendar event it published: absence from
                // a configuration file is not a publication decision (AI_GUIDELINE §13).
                List<SourceId> retired = [.. commit.PollingDisabled];
                List<ScheduleSource> rows = await dbContext.ScheduleSources
                    .Where(source => retired.Contains(source.SourceId))
                    .ToListAsync(cancellationToken);
                foreach (ScheduleSource row in rows)
                {
                    row.SetPollingEnabled(false);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return changed;
        });
    }

    public async Task<IReadOnlyList<ScheduleSourceCatalogRevisionSummary>> ListAsync(
        int limit,
        string currentContentHash,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        List<ScheduleSourceCatalogRevision> rows = await dbContext.ScheduleSourceCatalogRevisions
            .AsNoTracking()
            .OrderByDescending(revision => revision.RecordedAtUtc)
            .ThenByDescending(revision => revision.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(revision => Summarize(revision, currentContentHash))];
    }

    public async Task<ScheduleSourceCatalogRevisionDetail?> FindAsync(
        Guid id,
        string currentContentHash,
        CancellationToken cancellationToken)
    {
        ScheduleSourceCatalogRevision? revision = await dbContext.ScheduleSourceCatalogRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return revision is null
            ? null
            : new ScheduleSourceCatalogRevisionDetail
            {
                Summary = Summarize(revision, currentContentHash),
                Content = revision.Content,
            };
    }

    private static ScheduleSourceCatalogRevisionSummary Summarize(
        ScheduleSourceCatalogRevision revision,
        string currentContentHash) => new()
        {
            Id = revision.Id,
            Kind = revision.Kind.ToString(),
            RecordedAtUtc = revision.RecordedAtUtc,
            ContentHash = revision.ContentHash,
            PreviousContentHash = revision.PreviousContentHash,
            SourceCount = revision.SourceCount,
            ActorUserId = revision.ActorUserId,
            ActorEmail = revision.ActorEmail,
            Reason = revision.Reason,
            ChangeSummary = revision.ChangeSummary,

            // Compared against what the file holds now, not against the newest row: a document
            // changed outside the panel must not be presented as the last confirmed revision.
            IsCurrent = string.Equals(
                revision.ContentHash,
                currentContentHash,
                StringComparison.Ordinal),
        };
}
