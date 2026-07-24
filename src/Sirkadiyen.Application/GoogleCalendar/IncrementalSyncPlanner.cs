using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Decides, for one user and one changed lesson, which calendar operation keeps their calendar
/// correct (ADR-059). Pure and database-free so the affected-user rules can be reasoned about and
/// unit-tested; the fan-out (which users to ask about) is the service's store queries.
/// </summary>
/// <remarks>
/// The mapping ledger is the authority for what a user currently holds, so the two questions are
/// asked from opposite sides: for someone who already has the lesson, does the new record still
/// belong to them and has its content moved; for someone who does not, does the new record now
/// belong to them. A record that no longer applies — its audience narrowed, or it became
/// <see cref="CanonicalRecordStatus.Cancelled"/> — resolves to a delete, because
/// <see cref="CalendarAudienceResolver.Applies"/> already returns false for both.
/// </remarks>
public static class IncrementalSyncPlanner
{
    /// <summary>
    /// The operation for a user who already holds this lesson, given the new record and the content
    /// hash last written to their calendar.
    /// </summary>
    public static IncrementalCalendarOperation PlanForExistingHolder(
        CanonicalScheduleRecord currentRecord,
        StudentProfileView profile,
        string ledgerContentHash)
    {
        ArgumentNullException.ThrowIfNull(currentRecord);
        ArgumentNullException.ThrowIfNull(profile);

        if (!CalendarAudienceResolver.Applies(currentRecord, profile))
        {
            return IncrementalCalendarOperation.Delete;
        }

        return string.Equals(currentRecord.ContentHash, ledgerContentHash, StringComparison.Ordinal)
            ? IncrementalCalendarOperation.None
            : IncrementalCalendarOperation.Patch;
    }

    /// <summary>
    /// The operation for a cohort user who does not yet hold this lesson: an insert when the record
    /// now applies to them, otherwise nothing.
    /// </summary>
    public static IncrementalCalendarOperation PlanForCohortCandidate(
        CanonicalScheduleRecord currentRecord,
        StudentProfileView profile)
    {
        ArgumentNullException.ThrowIfNull(currentRecord);
        ArgumentNullException.ThrowIfNull(profile);

        return CalendarAudienceResolver.Applies(currentRecord, profile)
            ? IncrementalCalendarOperation.Insert
            : IncrementalCalendarOperation.None;
    }
}

public enum IncrementalCalendarOperation
{
    /// <summary>The calendar already matches; nothing to do.</summary>
    None,

    /// <summary>The lesson now applies and is not yet on the calendar.</summary>
    Insert,

    /// <summary>The lesson still applies but its content changed.</summary>
    Patch,

    /// <summary>The lesson no longer applies (audience narrowed or cancelled) and must be removed.</summary>
    Delete,
}
