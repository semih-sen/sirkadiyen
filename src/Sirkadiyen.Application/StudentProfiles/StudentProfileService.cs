using Sirkadiyen.Application.Licensing;

namespace Sirkadiyen.Application.StudentProfiles;

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

        // The program's academic year, not the schema's. Audience resolution
        // matches a canonical record to a student on it, so stamping the schema's
        // year on a program captured for a different one would leave that
        // student's calendar empty with nothing reporting a fault (ADR-103).
        // Validation has already established that the program exists.
        SupportedProfileProgram program = schema.FindProgram(
            submitted.ClassYear,
            submitted.ProgramLanguage)
            ?? throw new InvalidOperationException(
                "A validated profile named a program the schema does not define.");

        StudentProfileUpsertResult stored = await profileStore.UpsertAsync(
            userId,
            program.AcademicYear,
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
            Profile = stored.Profile,
            AudienceChanged = stored.AudienceChanged,
            CalendarResyncRequested = stored.CalendarResyncRequested,
        };
    }
}
