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

    public async Task<ScheduleSourceCatalogApplication> ApplyCatalogAsync(
        IReadOnlyCollection<ScheduleSource> sources,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        int changed = await ScheduleSourceUpsert.StageAsync(dbContext, sources, cancellationToken);
        ScheduleSourceRetirement retirement = await ScheduleSourceUpsert.StageRetirementsAsync(
            dbContext,
            sources,
            appliedAtUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ScheduleSourceCatalogApplication
        {
            RowsChanged = changed,
            Retired = retirement.Retired,
            Reinstated = retirement.Reinstated,
        };
    }

    /// <summary>
    /// Writes the completed cycle onto the row, or does nothing if the source is gone (ADR-155).
    /// </summary>
    /// <remarks>
    /// A missing row is not an error here, for the same reason it is not one when a failure is
    /// recorded: a source removed from the catalog while the cycle was running is a normal race.
    /// </remarks>
    public async Task RecordPollCompletedAsync(
        SourceId sourceId,
        DateTimeOffset polledAtUtc,
        CancellationToken cancellationToken)
    {
        ScheduleSource? source = await dbContext.ScheduleSources.SingleOrDefaultAsync(
            candidate => candidate.SourceId == sourceId,
            cancellationToken);

        if (source is null)
        {
            return;
        }

        // Never "changed": this cycle acquired nothing. What the source last changed is the
        // upload that stored its evidence, and it is a different question from whether the
        // pipeline is still running.
        source.RecordPolled(polledAtUtc, changed: false);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the failure onto the row, or does nothing if the source is gone (ADR-137).
    /// </summary>
    /// <remarks>
    /// A missing row is not an error here: this is called from a catch block, and a source removed
    /// from the catalog while the cycle was running is a normal race. Throwing would replace the
    /// failure being reported with a different one.
    /// </remarks>
    public async Task RecordPollFailureAsync(
        SourceId sourceId,
        DateTimeOffset failedAtUtc,
        string reason,
        CancellationToken cancellationToken)
    {
        ScheduleSource? source = await dbContext.ScheduleSources.SingleOrDefaultAsync(
            candidate => candidate.SourceId == sourceId,
            cancellationToken);

        if (source is null)
        {
            return;
        }

        source.RecordPollFailure(failedAtUtc, reason);
        await dbContext.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// Stages the retirement of every persisted source the catalog no longer declares, and the
    /// reinstatement of every retired source it declares again (ADR-155).
    /// </summary>
    /// <remarks>
    /// It reads rows the caller did not name, which is why it is a separate call rather than part
    /// of <see cref="StageAsync"/>: it is only ever correct when the collection passed to it is
    /// the whole catalog, and a caller applying part of one must not be able to retire the rest by
    /// omission.
    /// <para>
    /// Nothing is deleted. The row, its snapshots, its revisions and every calendar event it
    /// published survive a retirement exactly as they survived the polling being turned off
    /// (AI_GUIDELINE §13); what changes is that the source stops being presented as one of the
    /// pipeline's working parts.
    /// </para>
    /// </remarks>
    public static async Task<ScheduleSourceRetirement> StageRetirementsAsync(
        SirkadiyenDbContext dbContext,
        IReadOnlyCollection<ScheduleSource> sources,
        DateTimeOffset retiredAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(sources);

        HashSet<SourceId> declared = [.. sources.Select(static source => source.SourceId)];
        List<ScheduleSource> rows = await dbContext.ScheduleSources.ToListAsync(cancellationToken);

        List<SourceId> retired = [];
        List<SourceId> reinstated = [];
        foreach (ScheduleSource row in rows)
        {
            if (declared.Contains(row.SourceId))
            {
                if (row.Reinstate())
                {
                    reinstated.Add(row.SourceId);
                }

                continue;
            }

            if (row.Retire(retiredAtUtc))
            {
                retired.Add(row.SourceId);
            }
        }

        return new ScheduleSourceRetirement(retired, reinstated);
    }

    /// <summary>
    /// The fields the catalog owns, and therefore the fields a redeploy copies onto an existing
    /// row. Everything absent from it belongs to the row itself.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so it can be checked against the entity's own properties
    /// without a database (ADR-136): a field omitted here is invisible — the source keeps working
    /// and quietly ignores the configuration change — and that has happened twice.
    /// </remarks>
    internal static Dictionary<string, object?> ConfigurationOf(ScheduleSource source) => new()
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

        // And the discovery folder, which was omitted here when it was added and
        // failed in exactly the way the companions did (ADR-133, ADR-136). A new
        // row gets it, because the whole entity is inserted; an existing row never
        // did, so a running database kept a null folder and every poll quietly
        // acquired the catalogued document instead of the newest one. It is the
        // worst shape of this bug: nothing fails, the fallback warning cannot fire
        // because the source appears to declare no folder at all, and the rooms
        // simply stop being current the first week the faculty publishes a new
        // workbook.
        [nameof(ScheduleSource.DiscoveryFolderId)] = source.DiscoveryFolderId,

        // Whether the source publishes at all is catalog configuration too, and it decides how an
        // empty revision is read (ADR-156). Omitted here, a companion declared in the repository
        // would still raise an alarm on every running server.
        [nameof(ScheduleSource.PublishesSchedule)] = source.PublishesSchedule,
    };
}

/// <summary>Which sources one catalog application retired and which it brought back.</summary>
internal readonly record struct ScheduleSourceRetirement(
    IReadOnlyList<SourceId> Retired,
    IReadOnlyList<SourceId> Reinstated);
