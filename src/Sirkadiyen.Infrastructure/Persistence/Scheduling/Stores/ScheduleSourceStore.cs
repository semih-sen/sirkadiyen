using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

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

    public async Task<IReadOnlyList<ScheduleSource>> ListSharingDocumentAsync(
        SourceId sourceId,
        CancellationToken cancellationToken)
    {
        ScheduleSource? source = await FindAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return [];
        }

        if (source.SharedDocumentGroup is not { } group)
        {
            return [source];
        }

        return await dbContext.ScheduleSources
            .Where(candidate => candidate.SharedDocumentGroup == group)
            .OrderBy(candidate => candidate.SourceId)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> UpsertAsync(
        IReadOnlyCollection<ScheduleSource> sources,
        CancellationToken cancellationToken)
    {
        int changed = await ScheduleSourceUpsert.StageAsync(dbContext, sources, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return changed;
    }
}

/// <summary>
/// Stages a catalog's sources onto a context without saving, so the startup seed and the
/// administrative catalog edit apply configuration by exactly the same rules (ADR-114).
/// </summary>
/// <remarks>
/// The edit has to commit the upsert inside the transaction that records its revision, which is
/// why this is separate from <see cref="ScheduleSourceStore"/> rather than a call into it: two
/// copies of "which fields does the catalog own" would drift, and the field that stopped being
/// copied would be invisible until a source behaved as though the edit had never happened.
/// </remarks>
internal static class ScheduleSourceUpsert
{
    public static async Task<int> StageAsync(
        SirkadiyenDbContext dbContext,
        IReadOnlyCollection<ScheduleSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
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

        // The declared cohorts and the shared-document group are catalog-owned
        // too. Omitting them here would let an edited allowlist or a corrected
        // group name apply to a fresh database and silently not to a running one.
        [nameof(ScheduleSource.SupportedAudienceSelectors)] = source.SupportedAudienceSelectors,
        [nameof(ScheduleSource.AuthoritativeAudienceSelectors)] =
            source.AuthoritativeAudienceSelectors,
        [nameof(ScheduleSource.SharedDocumentGroup)] = source.SharedDocumentGroup,

        // The companions are catalog-owned for the same reason, and their
        // omission was invisible in a way the others would not have been: a
        // source that reads no companion parses successfully and publishes its
        // whole schedule, only without the topic the companion states. A
        // database seeded before a companion was declared kept an empty list
        // forever, and every Grade 3 bedside event reached a calendar with no
        // description at all (ADR-112).
        [nameof(ScheduleSource.CompanionSourceIds)] = source.CompanionSourceIds,

        // A rotation owner added to the catalog has to reach a running database
        // for the same reason: without it the annual source would keep publishing
        // every dissection hour after the group list had been uploaded (ADR-126).
        [nameof(ScheduleSource.GroupRotationSourceIds)] = source.GroupRotationSourceIds,
    };
}
