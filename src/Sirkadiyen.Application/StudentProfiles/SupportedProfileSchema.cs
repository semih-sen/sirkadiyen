using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// The server-owned definition of which academic profiles a student may declare,
/// derived from the confirmed source fixtures (ADR-048) and versioned (ADR-055).
/// </summary>
/// <remarks>
/// Both profile writes and, later, audience matching validate against this single
/// schema so a student can never select a cohort the sources do not publish.
/// <para>
/// Each program states the academic year its own sources were captured for, and
/// the schema states the year it was cut for. They agree until a rollover
/// begins, and during one they do not: the faculty publishes the new year one
/// grade at a time, so a schema cut for 2025-2026 legitimately carries a Grade 3
/// program confirmed for 2026-2027 (ADR-103).
/// </para>
/// </remarks>
public sealed record SupportedProfileSchema
{
    /// <summary>
    /// The academic year this schema revision was cut for. A student's profile is
    /// stamped with their <em>program's</em> year, not this one.
    /// </summary>
    public required string AcademicYear { get; init; }

    public required string SchemaVersion { get; init; }

    public required IReadOnlyList<SupportedProfileProgram> Programs { get; init; }

    public SupportedProfileProgram? FindProgram(int classYear, ProgramLanguage programLanguage) =>
        Programs.FirstOrDefault(
            program => program.ClassYear == classYear
                && program.ProgramLanguage == programLanguage);
}
