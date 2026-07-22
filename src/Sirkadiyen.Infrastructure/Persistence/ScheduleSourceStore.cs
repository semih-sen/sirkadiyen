using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence;

public sealed class ScheduleSourceStore(SirkadiyenDbContext dbContext) : IScheduleSourceStore
{
    public async Task<IReadOnlyList<ScheduleSource>> ListAsync(
        bool onlyPollingEnabled,
        CancellationToken cancellationToken)
    {
        IQueryable<ScheduleSource> query = dbContext.ScheduleSources;
        if (onlyPollingEnabled)
        {
            query = query.Where(source => source.IsPollingEnabled);
        }

        return await query
            .OrderBy(source => source.SourceId)
            .ToListAsync(cancellationToken);
    }

    public Task<ScheduleSource?> FindAsync(SourceId sourceId, CancellationToken cancellationToken) =>
        dbContext.ScheduleSources.SingleOrDefaultAsync(
            source => source.SourceId == sourceId,
            cancellationToken);

    public async Task<int> UpsertAsync(
        IReadOnlyCollection<ScheduleSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        List<SourceId> identifiers = [.. sources.Select(static source => source.SourceId)];
        Dictionary<SourceId, ScheduleSource> existing = await dbContext.ScheduleSources
            .Where(source => identifiers.Contains(source.SourceId))
            .ToDictionaryAsync(source => source.SourceId, cancellationToken);

        int changed = 0;
        foreach (ScheduleSource source in sources)
        {
            if (existing.TryGetValue(source.SourceId, out ScheduleSource? current))
            {
                // Only the fields the catalog owns are copied. The row's own
                // identifier and its polling history belong to the database: a
                // redeploy must not reset what the worker has observed.
                dbContext.Entry(current).CurrentValues.SetValues(ConfigurationOf(source));
                changed += dbContext.Entry(current).State is EntityState.Modified ? 1 : 0;
                continue;
            }

            dbContext.ScheduleSources.Add(source);
            changed++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private static Dictionary<string, object?> ConfigurationOf(ScheduleSource source) => new()
    {
        [nameof(ScheduleSource.DisplayName)] = source.DisplayName,
        [nameof(ScheduleSource.Transport)] = source.Transport,
        [nameof(ScheduleSource.DocumentFormat)] = source.DocumentFormat,
        [nameof(ScheduleSource.SourceUri)] = source.SourceUri,
        [nameof(ScheduleSource.ExternalId)] = source.ExternalId,
        [nameof(ScheduleSource.SheetGid)] = source.SheetGid,
        [nameof(ScheduleSource.ParserProfile)] = source.ParserProfile,
        [nameof(ScheduleSource.ParserProfileVersion)] = source.ParserProfileVersion,
        [nameof(ScheduleSource.AcademicYear)] = source.AcademicYear,
        [nameof(ScheduleSource.ClassYear)] = source.ClassYear,
        [nameof(ScheduleSource.ProgramLanguage)] = source.ProgramLanguage,
        [nameof(ScheduleSource.TimeZoneId)] = source.TimeZoneId,
    };
}
