using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.StudentProfiles;

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
