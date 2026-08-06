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
/// Only cohorts confirmed by a committed, current-year fixture appear here
/// (ADR-048). Grade 1 anatomy and Grade 3 selectors are deliberately absent
/// until their sources are captured and their profiles implemented. Grade 2
/// English practice is now evidenced, but that program stays absent until its
/// annual group-labelled rows and shared vertical-corridor sessions have safe
/// audience handling (ADR-084); adding it sooner would expose an incomplete or
/// over-broad calendar as complete.
/// </para>
/// </remarks>
public static class CurrentSupportedProfileSchema
{
    public const string AcademicYear = "2025-2026";

    /// <summary>
    /// Bumped to 1.1 when Grade 2 Turkish was added (ADR-079). It is recorded on
    /// every stored profile, so a profile written under 1.0 stays identifiable as
    /// one validated before Grade 2 existed.
    /// </summary>
    public const string SchemaVersion = "1.1";

    public static SupportedProfileSchema Create() => new()
    {
        AcademicYear = AcademicYear,
        SchemaVersion = SchemaVersion,
        Programs =
        [
            Grade1Turkish(),
            Grade1English(),
            Grade2Turkish(),
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
    /// </remarks>
    private static SupportedProfileProgram Grade2Turkish() => new()
    {
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
