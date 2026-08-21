using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// Reads the dates a group rotation's owning sources have published, from
/// PostgreSQL (ADR-126).
/// </summary>
public sealed class GroupRotationCoverageStore(SirkadiyenDbContext dbContext)
    : IGroupRotationCoverageStore
{
    public async Task<IReadOnlyList<DateOnly>> ListPublishedDatesAsync(
        IReadOnlyCollection<SourceId> rotationSourceIds,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rotationSourceIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);

        if (rotationSourceIds.Count == 0)
        {
            return [];
        }

        // Only a published revision counts. A parsed-but-unpublished group list
        // says nothing to a student yet, and treating it as coverage would remove
        // the fallback events before anything replaced them.
        List<string> owners = [.. rotationSourceIds.Select(static id => id.Value)];
        IQueryable<DateOnly> query =
            from record in dbContext.CanonicalScheduleRecords.AsNoTracking()
            join revision in dbContext.ScheduleRevisions.AsNoTracking()
                on record.ScheduleRevisionId equals revision.Id
            where revision.State == RevisionState.Published
                && record.RecordStatus == CanonicalRecordStatus.Scheduled
                && owners.Contains(record.SourceId.Value)
                && record.AcademicYear == academicYear
                && record.ClassYear == classYear
                && record.ProgramLanguage == programLanguage
            select record.LocalDate;

        // Ordered in the database, so the value that reaches the parse-run key is
        // the same whatever order rows come back in: an unordered set would make
        // the run's fingerprint differ from itself and reparse forever.
        return await query.Distinct().OrderBy(static date => date).ToListAsync(cancellationToken);
    }
}
