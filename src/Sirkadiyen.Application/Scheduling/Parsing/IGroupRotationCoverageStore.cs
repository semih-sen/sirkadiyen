using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Parsing;

/// <summary>
/// Reads which dates the sources that own a group rotation have published
/// (ADR-126).
/// </summary>
/// <remarks>
/// The Grade 2 annual workbooks state all three dissection hours of a session
/// and defer to the anatomy group lists, which assign each student one of them.
/// Until a list is uploaded, that deferral leaves a student with no dissection at
/// all, so the annual source publishes every hour of the dates no list covers —
/// and this is what says which dates those are.
/// <para>
/// It is a read over published truth rather than over sources or snapshots: a
/// group list that was acquired but whose revision has not been published states
/// nothing to a student yet, so it must not silence the fallback either.
/// </para>
/// </remarks>
public interface IGroupRotationCoverageStore
{
    /// <summary>
    /// The local dates the named sources' currently-published revisions state
    /// for the given program, ascending and distinct.
    /// </summary>
    /// <remarks>
    /// The program is the deferring source's own, not the owner's. An owner still
    /// pointing at a previous year's document publishes nothing for this program
    /// and therefore covers nothing, which is exactly the state a rollover leaves
    /// behind and exactly when the fallback has to stay on (ADR-115).
    /// </remarks>
    Task<IReadOnlyList<DateOnly>> ListPublishedDatesAsync(
        IReadOnlyCollection<SourceId> rotationSourceIds,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        CancellationToken cancellationToken);
}
