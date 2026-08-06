using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.StudentProfiles;

public sealed record SaveStudentProfileRequest
{
    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    public string? StudentNumber { get; init; }

    public IReadOnlyDictionary<string, string>? Selectors { get; init; }
}

public sealed record SaveStudentProfileResponse
{
    public required StudentProfileView Profile { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }

    /// <summary>
    /// Whether the change altered the audience the profile resolves and therefore queued a
    /// calendar re-synchronization (ADR-096).
    /// </summary>
    /// <remarks>
    /// It says the work was <em>requested</em>, not that it has happened: the worker converges the
    /// calendar on its next cycle. It is false for a first profile and for a change confined to
    /// fields the audience rule does not read.
    /// </remarks>
    public required bool CalendarResyncRequested { get; init; }
}

/// <summary>The supported profile options the frontend renders its form from.</summary>
public sealed record SupportedProfileOptionsResponse
{
    public required string AcademicYear { get; init; }

    public required string SchemaVersion { get; init; }

    public required IReadOnlyList<SupportedProfileProgram> Programs { get; init; }

    public static SupportedProfileOptionsResponse From(SupportedProfileSchema schema) => new()
    {
        AcademicYear = schema.AcademicYear,
        SchemaVersion = schema.SchemaVersion,
        Programs = schema.Programs,
    };
}
