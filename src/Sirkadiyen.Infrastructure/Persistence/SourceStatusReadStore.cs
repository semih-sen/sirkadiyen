using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Projects each configured source's ingestion health from existing pipeline tables: poll status
/// on the source row, the latest parse run (joined through its snapshot), and the latest revision.
/// The source catalog is small and this is an infrequent admin view, so it composes per source for
/// clarity rather than one large join.
/// </summary>
public sealed class SourceStatusReadStore(SirkadiyenDbContext dbContext) : ISourceStatusReadStore
{
    private const int RecentSnapshotLimit = 10;

    public async Task<IReadOnlyList<SourceStatusListItem>> ListAsync(
        CancellationToken cancellationToken)
    {
        List<ScheduleSource> sources = await dbContext.ScheduleSources
            .AsNoTracking()
            .OrderBy(source => source.ClassYear)
            .ThenBy(source => source.DisplayName)
            .ToListAsync(cancellationToken);

        List<SourceStatusListItem> items = new(sources.Count);
        foreach (ScheduleSource source in sources)
        {
            items.Add(await BuildListItemAsync(source, cancellationToken));
        }

        return items;
    }

    public async Task<SourceStatusDetail?> FindAsync(
        string sourceId,
        CancellationToken cancellationToken)
    {
        if (!SourceId.TryParse(sourceId, out SourceId parsed))
        {
            return null;
        }

        ScheduleSource? source = await dbContext.ScheduleSources
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.SourceId == parsed, cancellationToken);

        if (source is null)
        {
            return null;
        }

        List<SourceSnapshotSummary> snapshots = await dbContext.SourceSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.SourceId == parsed)
            .OrderByDescending(snapshot => snapshot.AcquiredAtUtc)
            .Take(RecentSnapshotLimit)
            .Select(snapshot => new SourceSnapshotSummary
            {
                SnapshotId = snapshot.Id,
                AcquiredAtUtc = snapshot.AcquiredAtUtc,
                ContentHash = snapshot.ContentHash,
                WorksheetCount = snapshot.WorksheetCount,
                CellCount = snapshot.CellCount,
                DiagnosticCount = snapshot.DiagnosticCount,
                HasPayload = snapshot.Payload != null,
            })
            .ToListAsync(cancellationToken);

        return new SourceStatusDetail
        {
            Summary = await BuildListItemAsync(source, cancellationToken),
            ParserProfile = source.ParserProfile,
            ParserProfileVersion = source.ParserProfileVersion,
            RecentSnapshots = snapshots,
        };
    }

    private async Task<SourceStatusListItem> BuildListItemAsync(
        ScheduleSource source,
        CancellationToken cancellationToken)
    {
        var latestRun = await (
                from run in dbContext.ParseRuns.AsNoTracking()
                join snapshot in dbContext.SourceSnapshots.AsNoTracking()
                    on run.SourceSnapshotId equals snapshot.Id
                where snapshot.SourceId == source.SourceId
                orderby run.StartedAtUtc descending
                select new
                {
                    run.Status,
                    run.StartedAtUtc,
                    run.CompletedAtUtc,
                    run.WarningCount,
                    run.ErrorCount,
                })
            .FirstOrDefaultAsync(cancellationToken);

        var latestRevision = await dbContext.ScheduleRevisions
            .AsNoTracking()
            .Where(revision => revision.SourceId == source.SourceId)
            .OrderByDescending(revision => revision.CreatedAtUtc)
            .Select(revision => new
            {
                revision.Id,
                revision.State,
                revision.CreatedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new SourceStatusListItem
        {
            SourceId = source.SourceId.Value,
            DisplayName = source.DisplayName,
            ClassYear = source.ClassYear,
            ProgramLanguage = source.ProgramLanguage,
            Transport = source.Transport,
            IsPollingEnabled = source.IsPollingEnabled,
            LastPolledAtUtc = source.LastPolledAtUtc,
            LastChangedAtUtc = source.LastChangedAtUtc,
            LatestParseRunStatus = latestRun?.Status,
            LatestParseRunAtUtc = latestRun?.CompletedAtUtc ?? latestRun?.StartedAtUtc,
            LatestParseWarningCount = latestRun?.WarningCount,
            LatestParseErrorCount = latestRun?.ErrorCount,
            LatestRevisionId = latestRevision?.Id,
            LatestRevisionState = latestRevision?.State,
            LatestRevisionAtUtc = latestRevision?.CreatedAtUtc,
        };
    }
}
