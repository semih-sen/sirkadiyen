using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Guards the server-owned supported-profile schema. It must be internally
/// well-formed and, per ADR-048, allow only cohorts the committed source catalog
/// actually declares, so a student can never select a value nothing publishes.
/// </summary>
public sealed class CurrentSupportedProfileSchemaTests
{
    private static readonly SupportedProfileSchema Schema = CurrentSupportedProfileSchema.Create();

    [Fact]
    public void SchemaCarriesTheCurrentYearAndVersion()
    {
        Assert.Equal("2025-2026", Schema.AcademicYear);
        Assert.Equal("1.0", Schema.SchemaVersion);
        Assert.NotEmpty(Schema.Programs);
    }

    [Fact]
    public void EveryDimensionIsEitherIndependentOrDependentButNotBoth()
    {
        foreach (SupportedProfileProgram program in Schema.Programs)
        {
            foreach (SupportedProfileDimension dimension in program.Dimensions)
            {
                Assert.False(string.IsNullOrWhiteSpace(dimension.Key));

                bool independent = dimension is
                {
                    Values: not null,
                    DependsOn: null,
                    ValuesByParent: null,
                };
                bool dependent = dimension is
                {
                    Values: null,
                    DependsOn: not null,
                    ValuesByParent: not null,
                };
                Assert.True(
                    independent ^ dependent,
                    $"Dimension '{dimension.Key}' must be exactly one of independent or dependent.");

                Assert.NotEmpty(dimension.AllPossibleValues());
            }
        }
    }

    [Fact]
    public void EveryDependencyPointsAtAnIndependentDimensionInTheSameProgram()
    {
        foreach (SupportedProfileProgram program in Schema.Programs)
        {
            foreach (SupportedProfileDimension dimension in program.Dimensions)
            {
                if (dimension.DependsOn is not { } parentKey)
                {
                    continue;
                }

                SupportedProfileDimension? parent = program.FindDimension(parentKey);
                Assert.NotNull(parent);
                Assert.False(parent.IsDependent, "A dependency chain deeper than one is not modelled.");

                // Every parent value that gates child values must itself be a value
                // the parent allows; a stray key would silently allow nothing.
                foreach (string parentValue in dimension.ValuesByParent!.Keys)
                {
                    Assert.Contains(parentValue, parent.AllPossibleValues());
                }
            }
        }
    }

    [Fact]
    public async Task EverySelectorValueIsDeclaredByAConfirmedSourceForThatCohort()
    {
        ScheduleSourceCatalog catalog = await LoadCatalogAsync();

        foreach (SupportedProfileProgram program in Schema.Programs)
        {
            IReadOnlyDictionary<string, HashSet<string>> declared = DeclaredSelectors(
                catalog,
                program.ClassYear,
                program.ProgramLanguage);

            foreach (SupportedProfileDimension dimension in program.Dimensions)
            {
                Assert.True(
                    declared.TryGetValue(dimension.Key, out HashSet<string>? sourceValues),
                    $"No source for class {program.ClassYear} {program.ProgramLanguage} declares "
                    + $"dimension '{dimension.Key}'.");

                foreach (string value in dimension.AllPossibleValues())
                {
                    Assert.Contains(value, sourceValues!);
                }
            }
        }
    }

    private static IReadOnlyDictionary<string, HashSet<string>> DeclaredSelectors(
        ScheduleSourceCatalog catalog,
        int classYear,
        ProgramLanguage programLanguage)
    {
        Dictionary<string, HashSet<string>> declared = new(StringComparer.Ordinal);
        IEnumerable<ScheduleSourceDefinition> cohortSources = catalog.Sources.Where(
            source => source.ClassYear == classYear
                && source.ProgramLanguage == programLanguage
                && source.SupportedAudienceSelectors is not null);

        foreach (ScheduleSourceDefinition source in cohortSources)
        {
            foreach ((string dimension, IReadOnlyList<string> values)
                in source.SupportedAudienceSelectors!)
            {
                if (!declared.TryGetValue(dimension, out HashSet<string>? set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    declared[dimension] = set;
                }

                foreach (string value in values)
                {
                    set.Add(value);
                }
            }
        }

        return declared;
    }

    private static Task<ScheduleSourceCatalog> LoadCatalogAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "schedule-sources.json");
        return new ScheduleSourceCatalogLoader().LoadAsync(path, CancellationToken.None);
    }
}
