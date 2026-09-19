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
/// calendar as complete. The Grade 3 English program was absent for the reason its
/// annual parser records — that program states no A/B division at all (ADR-098) —
/// but it is now onboardable. It first declared its microbiology/pathology group
/// alone, the one cohort the shared practice document divided it into (ADR-145);
/// as of 2026-2027 the faculty also split the English students into the
/// faculty-practice cohorts a1-a4, published on the A-group faculty document now
/// catalogued for English (G3-EN-A-FACULTY), so the program declares
/// facultyPracticeGroup too. It stays without a curriculum group, because the
/// English program has no A/B division of its own — the whole class sits its theory
/// together (ADR-098 stands on that point; ADR-160 adds only the faculty cohort).
/// </para>
/// <para>
/// A program states the academic year its own sources were captured for,
/// because during a rollover the programs do not share one (ADR-103, ADR-115).
/// They agree again as of ADR-131: the Grade 1 workbooks were the last
/// declared program still on 2025-2026, and moved with their catalog entries.
/// The per-program field stays, because the next rollover separates them
/// again.
/// </para>
/// </remarks>
public static class CurrentSupportedProfileSchema
{
    /// <summary>The year this schema revision was cut for.</summary>
    /// <remarks>
    /// Every declared program is on it again. Grade 3 arrived on 2026-2027
    /// (ADR-103), Grade 2 Turkish joined when its annual and practice sources
    /// moved (ADR-115), and Grade 1 Turkish and English followed when theirs did
    /// (ADR-131), so the separate <c>RolledOverAcademicYear</c> that the rollover
    /// needed while the programs disagreed is folded back into this one.
    /// <para>
    /// A program still states its own year, and that per-program field is the one
    /// that matters: it is what <see cref="StudentProfileService"/> stamps on the
    /// profile, and <c>CalendarAudienceResolver</c> matches a canonical record to
    /// a student on it. This constant and the catalog's <c>academicYear</c> for
    /// the same cohort must move together. Once they did not, and every Grade 2
    /// Turkish calendar silently stopped receiving lessons (ADR-115).
    /// </para>
    /// </remarks>
    public const string AcademicYear = "2026-2027";

    /// <summary>
    /// Bumped to 1.1 when Grade 2 Turkish was added (ADR-079), to 1.2 when
    /// Grade 3 Turkish arrived and each program began stating its own academic
    /// year (ADR-103), to 1.3 when Grade 2 Turkish rolled over to 2026-2027
    /// (ADR-115), to 1.4 when Grade 1 Turkish and English rolled over with their
    /// own sources (ADR-131), to 1.5 when the microbiology/pathology practice
    /// program added the <c>microPathologyGroup</c> dimension to Grade 3 Turkish
    /// and opened Grade 3 English (ADR-145), and to 1.6 when the faculty split the
    /// Grade 3 English students into the faculty-practice cohorts <c>A1</c>-<c>A4</c>,
    /// so that program gained <c>facultyPracticeGroup</c> — independent, since English
    /// has no curriculum group for it to depend on (ADR-160). It is recorded on every
    /// stored profile, so a profile written under an earlier version stays
    /// identifiable — and a Grade 3 English profile still on 1.5 is identifiable as one
    /// written before the new dimension existed and therefore missing it, which is
    /// exactly what the roster-profile audit fills in (ADR-159/ADR-160).
    /// </summary>
    public const string SchemaVersion = "1.6";

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
            Grade3English(),
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
    /// re-pointed at the new year's workbooks (ADR-115). Its anatomy sources are
    /// catalogued for that year now too but still await their upload, and the
    /// vertical-corridor pair has not moved at all (ADR-129), so the three
    /// selectors are evidenced — by the catalog as a whole rather than by the new
    /// year's documents alone — and the lessons those sources publish stay absent
    /// from a 2026-2027 calendar until their own documents are captured.
    /// </para>
    /// </remarks>
    private static SupportedProfileProgram Grade2Turkish() => new()
    {
        AcademicYear = AcademicYear,
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
    /// the faculty-practice cohort within it, and the independent microbiology/pathology
    /// group.
    /// </summary>
    /// <remarks>
    /// The curriculum group and the faculty-practice cohort are dependent, not
    /// independent, and this is the one place that matters: the A and B programs are
    /// separate documents with separate rotations, and a cohort number means a
    /// different rotation in each. A student in <c>3-A</c> may only be one of
    /// <c>A1</c>-<c>A8</c>, so the cohort is offered per group rather than as sixteen
    /// flat values that would let someone in the A program declare a B rotation
    /// (ADR-099). Both are required: every Grade 3 Turkish lesson is published to one
    /// curriculum group or both, and every faculty-practice session to exactly one
    /// cohort, so a student who declared neither would receive nothing.
    /// </remarks>
    private static SupportedProfileProgram Grade3Turkish() => new()
    {
        AcademicYear = AcademicYear,
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
            MicroPathologyGroup(),
        ],
    };

    /// <summary>
    /// Grade 3 English: the faculty-practice cohort and the independent
    /// microbiology/pathology group — and, unlike Grade 3 Turkish, no curriculum group.
    /// </summary>
    /// <remarks>
    /// The English program has no A/B division of its own (ADR-098): the whole class
    /// year sits its theoretical lessons together, and the English annual states no
    /// cohort. It first onboarded on its microbiology/pathology group alone, the one
    /// cohort the shared practice document divided it into (ADR-145). As of 2026-2027
    /// the faculty also split the English students into the faculty-practice cohorts
    /// <c>A1</c>-<c>A4</c> — the combined student list states them — so the program
    /// declares that group too (ADR-160). It is <b>independent</b> here, not dependent
    /// as it is for Turkish, precisely because there is no curriculum group to depend
    /// on: an English student carries a faculty-practice cohort and nothing gates it.
    /// The four values are <c>A1</c>-<c>A4</c> because that is where the faculty placed
    /// the English students; the A-group faculty document holds <c>A5</c>-<c>A8</c> too,
    /// but no English student is in them. The faculty-practice sessions reach an English
    /// student because the English faculty source publishes each record addressed by
    /// the cohort alone, with no curriculum-group selector the profile would have to
    /// match (ADR-160).
    /// </remarks>
    private static SupportedProfileProgram Grade3English() => new()
    {
        AcademicYear = AcademicYear,
        ClassYear = 3,
        ProgramLanguage = ProgramLanguage.English,
        Dimensions =
        [
            new SupportedProfileDimension
            {
                Key = "facultyPracticeGroup",
                Required = true,
                Values = [.. Enumerable.Range(1, 4).Select(index => $"A{index}")],
            },
            MicroPathologyGroup(),
        ],
    };

    /// <summary>
    /// The four microbiology/pathology practice cohorts the whole Grade 3 class is
    /// split into, in both programs (ADR-145).
    /// </summary>
    /// <remarks>
    /// It is independent of the curriculum group and the faculty-practice cohort: a
    /// student's A1/A2/B1/B2 assignment is stated by a single list covering both
    /// programs and does not follow from either other dimension, so it is a flat set
    /// of four values rather than a subdivision of anything. Required, because every
    /// Grade 3 student attends these practicals and a student who declared nothing
    /// would receive none of them.
    /// </remarks>
    private static SupportedProfileDimension MicroPathologyGroup() => new()
    {
        Key = "microPathologyGroup",
        Required = true,
        Values = ["A1", "A2", "B1", "B2"],
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
