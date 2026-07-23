using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// The confirmed supported-profile schema for the current academic year.
/// </summary>
/// <remarks>
/// This is server-owned reference data, not configuration a client may supply. It
/// is defined in code, unit-tested against the source catalog, and changes only
/// at academic-year rollover, which is a deployment anyway (ADR-055).
/// <para>
/// Only cohorts confirmed by a committed, current-year fixture appear here
/// (ADR-048). Grade 1 anatomy, Grade 2 and Grade 3 selectors are deliberately
/// absent until their sources are captured and their profiles implemented; adding
/// them here without evidence would let a student select a cohort nothing
/// publishes.
/// </para>
/// </remarks>
public static class CurrentSupportedProfileSchema
{
    public const string AcademicYear = "2025-2026";

    public const string SchemaVersion = "1.0";

    public static SupportedProfileSchema Create() => new()
    {
        AcademicYear = AcademicYear,
        SchemaVersion = SchemaVersion,
        Programs =
        [
            Grade1Turkish(),
            Grade1English(),
        ],
    };

    private static SupportedProfileProgram Grade1Turkish() => new()
    {
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
