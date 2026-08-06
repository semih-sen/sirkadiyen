using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>Persistence boundary for the single student profile a user owns.</summary>
public interface IStudentProfileStore
{
    Task<StudentProfileView?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or replaces the user's profile transactionally, and — in the same transaction —
    /// records a calendar re-synchronization request when the change alters the audience the
    /// profile resolves (ADR-096).
    /// </summary>
    /// <remarks>
    /// The two writes share one transaction on purpose: a profile that has moved to a new cohort
    /// while nothing knows the calendar must follow is exactly the state this feature exists to
    /// prevent.
    /// </remarks>
    Task<StudentProfileUpsertResult> UpsertAsync(
        Guid userId,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        string studentNumber,
        string selectorSchemaVersion,
        IReadOnlyDictionary<string, string> selectors,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
