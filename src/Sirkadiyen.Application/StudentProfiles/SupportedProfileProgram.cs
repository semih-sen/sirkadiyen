using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>The supported profile for one class year and program language.</summary>
public sealed record SupportedProfileProgram
{
    /// <summary>
    /// The academic year whose sources confirm this program's cohorts (ADR-103).
    /// </summary>
    /// <remarks>
    /// It belongs to the program rather than to the schema because the faculty
    /// publishes one grade at a time: when this was written the Grade 3 programs
    /// were captured for 2026-2027 while Grades 1 and 2 were still 2025-2026. It
    /// is not cosmetic. A canonical record reaches a student only when its
    /// academic year equals the one stamped on their profile, so a Grade 3
    /// student stamped with the schema-wide year would receive an empty calendar
    /// while every check downstream reported success.
    /// </remarks>
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required IReadOnlyList<SupportedProfileDimension> Dimensions { get; init; }

    public SupportedProfileDimension? FindDimension(string key) =>
        Dimensions.FirstOrDefault(dimension =>
            string.Equals(dimension.Key, key, StringComparison.Ordinal));
}
