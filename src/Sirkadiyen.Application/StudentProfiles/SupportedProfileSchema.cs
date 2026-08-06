using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// The server-owned definition of which academic profiles a student may declare,
/// derived from the confirmed source fixtures (ADR-048) and versioned (ADR-055).
/// </summary>
/// <remarks>
/// It covers exactly one current academic year, because there is one at a time
/// and a workbook capture confirms one year's cohorts. Both profile writes and,
/// later, audience matching validate against this single schema so a student can
/// never select a cohort the sources do not publish.
/// </remarks>
public sealed record SupportedProfileSchema
{
    public required string AcademicYear { get; init; }

    public required string SchemaVersion { get; init; }

    public required IReadOnlyList<SupportedProfileProgram> Programs { get; init; }

    public SupportedProfileProgram? FindProgram(int classYear, ProgramLanguage programLanguage) =>
        Programs.FirstOrDefault(
            program => program.ClassYear == classYear
                && program.ProgramLanguage == programLanguage);
}
