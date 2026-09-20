using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Everything <see cref="CalendarAudienceResolver"/> needs to decide whether a lesson belongs
/// to somebody: the program dimensions and the declared cohort selectors, and nothing else.
/// </summary>
/// <remarks>
/// A stored profile also carries a user id, a student number, a schema version and a write
/// timestamp, none of which the rule reads. Naming the audience separately lets a caller that
/// has no student — the cohort schedule simulation — ask the same question without inventing
/// those four values, which would be a falsehood written into data the rule then trusts.
/// </remarks>
public sealed record CalendarAudience
{
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required IReadOnlyDictionary<string, string> Selectors { get; init; }

    /// <summary>The audience a stored profile resolves to.</summary>
    public static CalendarAudience From(StudentProfileView profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new CalendarAudience
        {
            AcademicYear = profile.AcademicYear,
            ClassYear = profile.ClassYear,
            ProgramLanguage = profile.ProgramLanguage,
            Selectors = profile.Selectors,
        };
    }
}
