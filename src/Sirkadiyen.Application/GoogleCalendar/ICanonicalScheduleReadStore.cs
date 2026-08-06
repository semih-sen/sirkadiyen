using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

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

    /// <summary>
    /// The <c>(SourceId, StableIdentity)</c> pairs every currently-published revision of one
    /// academic year states, across programs (ADR-096).
    /// </summary>
    /// <remarks>
    /// This answers "is this lesson still live" for a ledger row, which the row's
    /// <c>CanonicalRecordId</c> cannot: that id points at whichever revision wrote the event, and
    /// an <c>Unchanged</c> diff entry never advances it, so a republished-but-identical lesson
    /// would look retired. The stable identity is what survives revisions, so it is the join key.
    /// <para>
    /// A profile re-synchronization uses it as the boundary on its deletions: a mapping absent
    /// from this set is left completely alone, because removing it would be deleting from absence
    /// rather than from a published decision (AI_GUIDELINE §13).
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<PublishedRecordIdentity>> ListCurrentPublishedIdentitiesAsync(
        string academicYear,
        CancellationToken cancellationToken);
}

/// <summary>One live lesson's identity, as the ledger keys it.</summary>
public sealed record PublishedRecordIdentity
{
    public required SourceId SourceId { get; init; }

    public required string StableIdentity { get; init; }
}
