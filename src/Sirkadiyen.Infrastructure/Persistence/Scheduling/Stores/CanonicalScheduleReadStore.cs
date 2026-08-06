using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Reads the live canonical schedule for a program from PostgreSQL, for initial sync (ADR-058).
/// </summary>
public sealed class CanonicalScheduleReadStore(SirkadiyenDbContext dbContext)
    : ICanonicalScheduleReadStore
{
    public async Task<IReadOnlyList<CanonicalScheduleRecord>> ListCurrentPublishedRecordsAsync(
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        CancellationToken cancellationToken)
    {
        // A source has at most one published revision at a time — publishing supersedes the
        // previous one — so joining on Published yields exactly the live schedule. Several
        // sources can target the same program (for example the annual and practice tables), and
        // all of their scheduled records are returned; cohort filtering is a separate step.
        IQueryable<CanonicalScheduleRecord> query =
            from record in dbContext.CanonicalScheduleRecords.AsNoTracking()
            join revision in dbContext.ScheduleRevisions.AsNoTracking()
                on record.ScheduleRevisionId equals revision.Id
            where revision.State == RevisionState.Published
                && record.RecordStatus == CanonicalRecordStatus.Scheduled
                && record.AcademicYear == academicYear
                && record.ClassYear == classYear
                && record.ProgramLanguage == programLanguage
            select record;

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CanonicalScheduleRecord>> ListRecordsByIdsAsync(
        IReadOnlyCollection<Guid> recordIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        if (recordIds.Count == 0)
        {
            return [];
        }

        // Deletion entries reference records of a now-superseded revision, so this deliberately does
        // not filter on revision state or record status: it loads exactly the records the diff named.
        return await dbContext.CanonicalScheduleRecords
            .AsNoTracking()
            .Where(record => recordIds.Contains(record.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PublishedRecordIdentity>> ListCurrentPublishedIdentitiesAsync(
        string academicYear,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);

        // Across every program of the year, because the question this answers is "is this lesson
        // still live" for a ledger row that may have been written under a different cohort than
        // the student now belongs to. Only the identity is projected: a profile re-synchronization
        // needs to know that the lesson exists, not what it says.
        return await (
            from record in dbContext.CanonicalScheduleRecords.AsNoTracking()
            join revision in dbContext.ScheduleRevisions.AsNoTracking()
                on record.ScheduleRevisionId equals revision.Id
            where revision.State == RevisionState.Published
                && record.RecordStatus == CanonicalRecordStatus.Scheduled
                && record.AcademicYear == academicYear
            select new PublishedRecordIdentity
            {
                SourceId = record.SourceId,
                StableIdentity = record.StableIdentity,
            })
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
