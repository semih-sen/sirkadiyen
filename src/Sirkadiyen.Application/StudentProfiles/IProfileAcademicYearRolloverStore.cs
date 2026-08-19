using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// Reads the profiles an academic-year rollover would move, and applies the move (ADR-115).
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="IStudentProfileStore"/>, whose upsert is the path a
/// student's own save takes. A rollover changes only the academic year and schema version and
/// must never touch a selector or a student number, so it does not reuse an upsert that takes
/// all of them: passing a whole profile through would make a bug in the caller able to rewrite
/// a student's declared cohort.
/// <para>
/// It also never carries a refresh token. Planning and applying a rollover needs profiles and
/// ledger identities only, so no credential reaches the request path (AI_GUIDELINE §15).
/// </para>
/// </remarks>
public interface IProfileAcademicYearRolloverStore
{
    /// <summary>
    /// Every stored profile in the program still stamped with the year being left, with what its
    /// owner's calendar holds, ordered by user id so a plan — and the hash over it — is
    /// deterministic.
    /// </summary>
    /// <remarks>
    /// Unlike a calendar repair, this is not restricted to synchronization-ready connections. A
    /// profile whose owner has not connected a calendar, or whose initial sync never finished,
    /// still carries a year that decides what they will receive when they do, so leaving it on
    /// the old one would simply defer the same empty calendar.
    /// </remarks>
    Task<IReadOnlyList<ProfileRolloverCandidate>> ListCandidatesAsync(
        ProfileRolloverScope scope,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stored profiles in one program that carry any academic year other than the one the deployed
    /// schema states for it, oldest first, bounded (ADR-117).
    /// </summary>
    /// <remarks>
    /// Deliberately "any year other than", not "the year an operator named". The automatic
    /// reconciler has no operator to name one, and a profile stranded on a year nobody remembers
    /// is exactly the case that must not be skipped.
    /// <para>
    /// It returns no ledger holdings. Those exist so a plan can tell an operator how many lessons
    /// a rollover puts back; the reconciler is not asking anyone for a decision, so loading a
    /// thousand mapping rows per student to compute a number nobody reads would be work for its
    /// own sake.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<DriftedProfile>> ListDriftedAsync(
        int classYear,
        ProgramLanguage programLanguage,
        string expectedAcademicYear,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-stamps the named profiles with the target year and schema version and, in the same
    /// transaction, flags each owner's connection for the convergence pass that writes the new
    /// year's lessons (ADR-096).
    /// </summary>
    /// <remarks>
    /// One transaction for both, for the reason the profile upsert already shares one: a profile
    /// moved to a new year while nothing knows the calendar must follow is exactly the state that
    /// produced this incident.
    /// </remarks>
    Task<ProfileRolloverApplyResult> ApplyAsync(
        IReadOnlyCollection<Guid> userIds,
        string toAcademicYear,
        string toSchemaVersion,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

/// <summary>What one applied rollover actually wrote.</summary>
public sealed record ProfileRolloverApplyResult
{
    public required int ProfilesMoved { get; init; }

    public required int ConvergenceRequested { get; init; }
}

/// <summary>One stored profile carrying a year its program no longer states (ADR-117).</summary>
public sealed record DriftedProfile
{
    public required Guid UserId { get; init; }

    public required StudentProfileView Profile { get; init; }
}
