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

    /// <summary>
    /// The published revisions whose changes have not reached any calendar yet: no diff row, or a
    /// diff that is held, discarded, waiting or failed (ADR-166).
    /// </summary>
    /// <remarks>
    /// Being published makes a revision the live schedule, which is what a student's first
    /// synchronization writes and what every audience question is answered from. It does not by
    /// itself make it something to converge an <em>existing</em> calendar onto: that is the diff's
    /// decision, because only the diff knows which lesson replaced which, and therefore what has to
    /// be removed alongside what is added.
    /// <para>
    /// The periodic inventory sweep used to miss this. It repairs from published truth and never
    /// deletes from absence (ADR-089), so while a diff sat held it wrote the additions of that
    /// revision and none of its retirements — a reworded lesson appeared beside its old spelling in
    /// every affected calendar, and nothing could then remove the old one. Reading this set is how
    /// the sweep leaves an undispatched revision to the dispatch path.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Guid>> ListRevisionsAwaitingCalendarDispatchAsync(
        CancellationToken cancellationToken);
}

/// <summary>One live lesson's identity, as the ledger keys it.</summary>
public sealed record PublishedRecordIdentity
{
    public required SourceId SourceId { get; init; }

    public required string StableIdentity { get; init; }
}
