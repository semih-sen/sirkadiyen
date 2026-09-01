using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Infrastructure.Persistence.Operations.Stores;

/// <summary>
/// The stall queries, as five small reads over state the pipeline already keeps.
/// </summary>
/// <remarks>
/// Each one returns a count and the oldest item rather than the rows themselves:
/// this runs on a timer, and nothing here should ever pull a queue into memory to
/// measure it.
/// </remarks>
public sealed class PipelineStallReadStore(SirkadiyenDbContext dbContext)
    : IPipelineStallReadStore
{
    public Task<StalledWork> CountRevisionsAwaitingReviewAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken) =>
        SummarizeRevisionsAsync(
            dbContext.ScheduleRevisions
                .Where(revision => revision.State == RevisionState.ReviewRequired)
                .Where(revision => revision.CreatedAtUtc < cutoffUtc),
            cancellationToken);

    public Task<StalledWork> CountRevisionsStuckBeforeValidationAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken) =>
        SummarizeRevisionsAsync(
            dbContext.ScheduleRevisions
                // Validating is included deliberately: it is a transient state
                // inside one transaction, so a revision found sitting in it is a
                // worker that died mid-commit, not work in progress.
                .Where(revision =>
                    revision.State == RevisionState.Parsed
                    || revision.State == RevisionState.Validating)
                .Where(revision => revision.CreatedAtUtc < cutoffUtc),
            cancellationToken);

    public Task<StalledWork> CountDiffsAwaitingReleaseAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken) =>
        SummarizeDiffsAsync(
            dbContext.ScheduleDiffs
                .Where(diff => diff.State == ScheduleDiffState.Held)
                .Where(diff => diff.CreatedAtUtc < cutoffUtc),
            cancellationToken);

    public Task<StalledWork> CountFailedDispatchesAsync(CancellationToken cancellationToken) =>
        SummarizeDiffsAsync(
            dbContext.ScheduleDiffs
                .Where(diff => diff.CalendarDispatchState == CalendarDispatchState.Failed),
            cancellationToken);

    public async Task<StalledWork> CountSourcesNotPolledSinceAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        // Only sources that have been acquired before are considered. A source
        // that has never been acquired is a known gap in the catalog — a document
        // nobody has published yet, a transport nobody has written — and
        // reporting it every hour would train an operator to ignore the report.
        // A source that used to be acquired and is not any more is the opposite:
        // something that worked has stopped.
        var stalled = await dbContext.ScheduleSources
            .AsNoTracking()
            .Select(source => new
            {
                source.SourceId,
                LastAcquiredAtUtc = dbContext.SourceSnapshots
                    .Where(snapshot => snapshot.ScheduleSourceId == source.Id)
                    .Max(snapshot => (DateTimeOffset?)snapshot.AcquiredAtUtc),
            })
            .Where(row => row.LastAcquiredAtUtc != null && row.LastAcquiredAtUtc < cutoffUtc)
            .OrderBy(row => row.LastAcquiredAtUtc)
            .ToListAsync(cancellationToken);

        return stalled.Count == 0
            ? StalledWork.None
            : new StalledWork
            {
                Count = stalled.Count,
                OldestSinceUtc = stalled[0].LastAcquiredAtUtc,
                OldestSourceId = stalled[0].SourceId.Value,
            };
    }

    private static async Task<StalledWork> SummarizeRevisionsAsync(
        IQueryable<ScheduleRevision> query,
        CancellationToken cancellationToken)
    {
        int count = await query.CountAsync(cancellationToken);
        if (count == 0)
        {
            return StalledWork.None;
        }

        // Materialized before the source id is read: SourceId is a value object,
        // and reaching into it inside the query would be translated as a column
        // that does not exist.
        var oldest = await query
            .AsNoTracking()
            .OrderBy(revision => revision.CreatedAtUtc)
            .Select(revision => new { revision.CreatedAtUtc, revision.SourceId })
            .FirstAsync(cancellationToken);

        return new StalledWork
        {
            Count = count,
            OldestSinceUtc = oldest.CreatedAtUtc,
            OldestSourceId = oldest.SourceId.Value,
        };
    }

    private static async Task<StalledWork> SummarizeDiffsAsync(
        IQueryable<ScheduleDiff> query,
        CancellationToken cancellationToken)
    {
        int count = await query.CountAsync(cancellationToken);
        if (count == 0)
        {
            return StalledWork.None;
        }

        var oldest = await query
            .AsNoTracking()
            .OrderBy(diff => diff.CreatedAtUtc)
            .Select(diff => new { diff.CreatedAtUtc, diff.SourceId })
            .FirstAsync(cancellationToken);

        return new StalledWork
        {
            Count = count,
            OldestSinceUtc = oldest.CreatedAtUtc,
            OldestSourceId = oldest.SourceId.Value,
        };
    }
}
