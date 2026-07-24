using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Reads the currently-live canonical schedule for a program, for resolving which events
/// belong on a student's calendar (ADR-058).
/// </summary>
public interface ICanonicalScheduleReadStore
{
    /// <summary>
    /// The scheduled records of every source's currently-published revision that targets the
    /// given program. Superseded and unpublished revisions are excluded, so this is exactly the
    /// live schedule a student in that program should see; audience filtering by cohort is a
    /// separate, pure step.
    /// </summary>
    Task<IReadOnlyList<CanonicalScheduleRecord>> ListCurrentPublishedRecordsAsync(
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the canonical records a diff's entries reference by id (ADR-059). Incremental sync uses
    /// this to read the previous record of a deletion and the current record of a creation or update
    /// without re-loading whole revisions.
    /// </summary>
    Task<IReadOnlyList<CanonicalScheduleRecord>> ListRecordsByIdsAsync(
        IReadOnlyCollection<Guid> recordIds,
        CancellationToken cancellationToken);
}
