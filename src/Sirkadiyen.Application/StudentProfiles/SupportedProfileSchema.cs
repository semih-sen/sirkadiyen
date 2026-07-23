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

/// <summary>The supported profile for one class year and program language.</summary>
public sealed record SupportedProfileProgram
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required IReadOnlyList<SupportedProfileDimension> Dimensions { get; init; }

    public SupportedProfileDimension? FindDimension(string key) =>
        Dimensions.FirstOrDefault(dimension =>
            string.Equals(dimension.Key, key, StringComparison.Ordinal));
}

/// <summary>
/// One selector dimension a student chooses a value for, such as a practice group.
/// </summary>
/// <remarks>
/// A dimension is either independent, with an explicit <see cref="Values"/> list,
/// or dependent, naming a <see cref="DependsOn"/> parent and the child values
/// allowed for each parent value in <see cref="ValuesByParent"/>. The dependency
/// exists because a subgroup belongs to exactly one group: subgroup <c>A1</c> is
/// valid only when the group is <c>A</c>. The values themselves are evidence from
/// the source, never derived by string logic in the validator.
/// </remarks>
public sealed record SupportedProfileDimension
{
    public required string Key { get; init; }

    public required bool Required { get; init; }

    /// <summary>The allowed values for an independent dimension.</summary>
    public IReadOnlyList<string>? Values { get; init; }

    /// <summary>The parent dimension key, when this dimension is dependent.</summary>
    public string? DependsOn { get; init; }

    /// <summary>The allowed values keyed by the parent value, for a dependent dimension.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ValuesByParent { get; init; }

    public bool IsDependent => DependsOn is not null;

    /// <summary>
    /// The values this dimension allows given the chosen parent value, or an empty
    /// list when the dimension is dependent and the parent value is unknown.
    /// </summary>
    public IReadOnlyList<string> AllowedValuesFor(string? parentValue)
    {
        if (!IsDependent)
        {
            return Values ?? [];
        }

        if (parentValue is null || ValuesByParent is null)
        {
            return [];
        }

        return ValuesByParent.TryGetValue(parentValue, out IReadOnlyList<string>? values)
            ? values
            : [];
    }

    /// <summary>Every value this dimension could allow under any parent.</summary>
    public IReadOnlyList<string> AllPossibleValues()
    {
        if (!IsDependent)
        {
            return Values ?? [];
        }

        if (ValuesByParent is null)
        {
            return [];
        }

        return [.. ValuesByParent.Values.SelectMany(values => values)];
    }
}
