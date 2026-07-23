using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>Persistence boundary for the single student profile a user owns.</summary>
public interface IStudentProfileStore
{
    Task<StudentProfileView?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces the user's profile transactionally.</summary>
    Task<StudentProfileView> UpsertAsync(
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

/// <summary>A read projection of a stored student profile.</summary>
public sealed record StudentProfileView
{
    public required Guid UserId { get; init; }

    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string StudentNumber { get; init; }

    public required string SelectorSchemaVersion { get; init; }

    public required IReadOnlyDictionary<string, string> Selectors { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record SaveStudentProfileResult
{
    public required SaveStudentProfileOutcome Outcome { get; init; }

    public StudentProfileView? Profile { get; init; }

    public IReadOnlyList<StudentProfileValidationError> ValidationErrors { get; init; } = [];
}

public enum SaveStudentProfileOutcome
{
    Saved,
    Invalid,
    ActivationRequired,
}

/// <summary>
/// Coordinates validating a student profile against the supported schema and
/// persisting it, enforcing that the account is activated first.
/// </summary>
public sealed class StudentProfileService(
    SupportedProfileSchema schema,
    IStudentProfileStore profileStore,
    ILicenseStore licenseStore,
    TimeProvider timeProvider)
{
    public SupportedProfileSchema Schema => schema;

    public Task<StudentProfileView?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        profileStore.GetByUserIdAsync(userId, cancellationToken);

    public async Task<SaveStudentProfileResult> SaveAsync(
        Guid userId,
        SubmittedStudentProfile submitted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        // The onboarding order is enforced by the backend, not the UI: a profile
        // may only be set once the account is activated (guideline §6, §8). A
        // suspended or never-activated account cannot slip a profile in.
        UserLicenseState licenseState = await licenseStore.GetUserLicenseStateAsync(
            userId,
            cancellationToken);
        if (licenseState != UserLicenseState.Active)
        {
            return new SaveStudentProfileResult
            {
                Outcome = SaveStudentProfileOutcome.ActivationRequired,
            };
        }

        StudentProfileValidationResult validation = StudentProfileValidator.Validate(
            schema,
            submitted);
        if (!validation.IsValid)
        {
            return new SaveStudentProfileResult
            {
                Outcome = SaveStudentProfileOutcome.Invalid,
                ValidationErrors = validation.Errors,
            };
        }

        StudentProfileView stored = await profileStore.UpsertAsync(
            userId,
            schema.AcademicYear,
            submitted.ClassYear,
            submitted.ProgramLanguage,
            submitted.StudentNumber,
            schema.SchemaVersion,
            submitted.Selectors,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return new SaveStudentProfileResult
        {
            Outcome = SaveStudentProfileOutcome.Saved,
            Profile = stored,
        };
    }
}
