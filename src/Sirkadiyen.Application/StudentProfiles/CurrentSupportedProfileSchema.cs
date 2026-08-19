using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// The confirmed supported-profile schema for the current academic year.
/// </summary>
/// <remarks>
/// This is server-owned reference data, not configuration a client may supply. It
/// is defined in code, unit-tested against the source catalog, and changes only
/// at academic-year rollover, which is a deployment anyway (ADR-055).
/// <para>
/// Only cohorts confirmed by a committed fixture appear here (ADR-048). Grade 1
/// anatomy is deliberately absent until its source is captured. Grade 2 English
/// practice is now evidenced, but that program stays absent until its annual
/// group-labelled rows and shared vertical-corridor sessions have safe audience
/// handling (ADR-084); adding it sooner would expose an incomplete or over-broad
/// calendar as complete. The Grade 3 English program is absent for the reason
/// its parser records: that program states no A/B division at all, so it has no
/// selector to declare and nothing to onboard beyond class year (ADR-098).
/// </para>
/// <para>
/// The programs no longer share one academic year. The faculty published the
/// 2026-2027 Grade 3 documents while Grades 1 and 2 were still on 2025-2026, and
/// the Grade 2 Turkish annual and practice sources followed, so each program
/// states the year its own sources were captured for (ADR-103, ADR-115).
/// </para>
/// </remarks>
public static class CurrentSupportedProfileSchema
{
    /// <summary>The year this schema revision was cut for.</summary>
    public const string AcademicYear = "2025-2026";

    /// <summary>
    /// The year the rolled-over programs were captured for (ADR-103, ADR-115).
    /// </summary>
    /// <remarks>
    /// Grade 3 arrived on it, and Grade 2 Turkish joined it when its annual and
    /// practice sources were moved. A program's year is what
    /// <see cref="StudentProfileService"/> stamps on the profile, and
    /// <c>CalendarAudienceResolver</c> matches a canonical record to a student on
    /// it, so this constant and the catalog's <c>academicYear</c> for the same
    /// cohort must move together. Once they did not, and every Grade 2 Turkish
    /// calendar silently stopped receiving lessons (ADR-115).
    /// </remarks>
    public const string RolledOverAcademicYear = "2026-2027";

    /// <summary>
    /// Bumped to 1.1 when Grade 2 Turkish was added (ADR-079), to 1.2 when
    /// Grade 3 Turkish arrived and each program began stating its own academic
    /// year (ADR-103), and to 1.3 when Grade 2 Turkish rolled over to 2026-2027
    /// (ADR-115). It is recorded on every stored profile, so a profile written
    /// under an earlier version stays identifiable — and, for a Grade 2 profile
    /// still on 1.2, identifiable as one stamped before the rollover and
    /// therefore due for it.
    /// </summary>
    public const string SchemaVersion = "1.3";

    public static SupportedProfileSchema Create() => new()
    {
        AcademicYear = AcademicYear,
        SchemaVersion = SchemaVersion,
        Programs =
        [
            Grade1Turkish(),
            Grade1English(),
            Grade2Turkish(),
            Grade3Turkish(),
        ],
    };

    private static SupportedProfileProgram Grade1Turkish() => new()
    {
        AcademicYear = AcademicYear,
        ClassYear = 1,
        ProgramLanguage = ProgramLanguage.Turkish,
        Dimensions =
        [
            new SupportedProfileDimension
            {
                Key = "practiceGroup",
                Required = true,
                Values = ["A", "B", "C", "D", "E", "F", "G", "H"],
            },
            new SupportedProfileDimension
            {
                Key = "practiceSubgroup",
                Required = true,
                DependsOn = "practiceGroup",
                ValuesByParent = TwoSubgroupsEach("A", "B", "C", "D", "E", "F", "G", "H"),
            },
        ],
    };

    private static SupportedProfileProgram Grade1English() => new()
    {
        AcademicYear = AcademicYear,
        ClassYear = 1,
        ProgramLanguage = ProgramLanguage.English,
        Dimensions =
        [
            new SupportedProfileDimension
            {
                Key = "practiceGroup",
                Required = true,
                Values = ["İ"],
            },
            new SupportedProfileDimension
            {
                Key = "practiceSubgroup",
                Required = true,
                DependsOn = "practiceGroup",
                ValuesByParent = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["İ"] = ["İ1", "İ2", "İ3"],
                },
            },
        ],
    };

    /// <summary>
    /// Grade 2 Turkish: the same lettered practice cohorts as Grade 1, plus the
    /// anatomy group, which is a separate rotation a student also belongs to.
    /// </summary>
    /// <remarks>
    /// The practice sheet states groups <c>A</c>-<c>H</c> and the vertical-corridor
    /// calendar states both those groups and their subgroups, so the pair is
    /// evidenced across two sources (ADR-074, ADR-077). The anatomy group is
    /// independent of them: the dissection rotation assigns <c>1</c>, <c>2</c> or
    /// <c>3</c> to a student regardless of which letter they carry, so a Grade 2
    /// student declares three selectors rather than two (ADR-078, ADR-079).
    /// <para>
    /// The program moved to 2026-2027 when the annual and practice sources were
    /// re-pointed at the new year's workbooks (ADR-115). Its anatomy and
    /// vertical-corridor sources have not moved yet, so the three selectors are
    /// still evidenced — by the catalog as a whole rather than by the new year's
    /// sources alone — and the lessons those sources publish will be absent from
    /// a 2026-2027 calendar until their own documents are captured.
    /// </para>
    /// </remarks>
    private static SupportedProfileProgram Grade2Turkish() => new()
    {
        AcademicYear = RolledOverAcademicYear,
        ClassYear = 2,
        ProgramLanguage = ProgramLanguage.Turkish,
        Dimensions =
        [
            new SupportedProfileDimension
            {
                Key = "practiceGroup",
                Required = true,
                Values = ["A", "B", "C", "D", "E", "F", "G", "H"],
            },
            new SupportedProfileDimension
            {
                Key = "practiceSubgroup",
                Required = true,
                DependsOn = "practiceGroup",
                ValuesByParent = TwoSubgroupsEach("A", "B", "C", "D", "E", "F", "G", "H"),
            },
            new SupportedProfileDimension
            {
                Key = "anatomyGroup",
                Required = true,
                Values = ["1", "2", "3"],
            },
        ],
    };

    /// <summary>
    /// Grade 3 Turkish: the curriculum group the whole class year is split into,
    /// and the faculty-practice cohort within it.
    /// </summary>
    /// <remarks>
    /// The two dimensions are dependent, not independent, and this is the one
    /// place that matters: the A and B programs are separate documents with
    /// separate rotations, and a cohort number means a different rotation in
    /// each. A student in <c>3-A</c> may only be one of <c>A1</c>-<c>A8</c>, so
    /// the cohort is offered per group rather than as sixteen flat values that
    /// would let someone in the A program declare a B rotation (ADR-099).
    /// <para>
    /// Both are required. Every Grade 3 lesson is published to one curriculum
    /// group or both, and every faculty-practice session to exactly one cohort,
    /// so a student who declared neither would receive nothing.
    /// </para>
    /// </remarks>
    private static SupportedProfileProgram Grade3Turkish() => new()
    {
        AcademicYear = RolledOverAcademicYear,
        ClassYear = 3,
        ProgramLanguage = ProgramLanguage.Turkish,
        Dimensions =
        [
            new SupportedProfileDimension
            {
                Key = "curriculumGroup",
                Required = true,
                Values = ["3-A", "3-B"],
            },
            new SupportedProfileDimension
            {
                Key = "facultyPracticeGroup",
                Required = true,
                DependsOn = "curriculumGroup",
                ValuesByParent = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["3-A"] = [.. EightCohorts("A")],
                    ["3-B"] = [.. EightCohorts("B")],
                },
            },
        ],
    };

    /// <summary>The eight faculty-practice cohorts of one curriculum group.</summary>
    private static IEnumerable<string> EightCohorts(string letter) =>
        Enumerable.Range(1, 8).Select(index => $"{letter}{index}");

    /// <summary>Builds the two-subgroup-per-group map, for example A → A1, A2.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> TwoSubgroupsEach(
        params string[] groups)
    {
        Dictionary<string, IReadOnlyList<string>> map = new(StringComparer.Ordinal);
        foreach (string group in groups)
        {
            map[group] = [$"{group}1", $"{group}2"];
        }

        return map;
    }
}
