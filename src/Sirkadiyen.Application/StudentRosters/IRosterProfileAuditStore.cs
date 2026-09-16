using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// Reads the stored profiles a roster-profile audit checks against the published lists (ADR-159).
/// </summary>
/// <remarks>
/// Read-only on purpose. The audit corrects a profile through the student's own write path
/// (<see cref="StudentProfileService"/>), so this store never writes: it inherits every guard the
/// student's save has — the activation check, the schema validation, the ADR-096 audience/resync —
/// rather than a parallel upsert that could drift from them (the ADR-158 rule, applied to a batch).
/// <para>
/// It carries no refresh token and no ledger holdings. The audit needs a cohort's profiles and the
/// live lists only, so no credential reaches the request path (AI_GUIDELINE §15).
/// </para>
/// </remarks>
public interface IRosterProfileAuditStore
{
    /// <summary>
    /// Every stored profile in one program, ordered by user id so a plan — and the hash over it —
    /// is deterministic.
    /// </summary>
    Task<IReadOnlyList<StudentProfileView>> ListCohortProfilesAsync(
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        CancellationToken cancellationToken);
}
