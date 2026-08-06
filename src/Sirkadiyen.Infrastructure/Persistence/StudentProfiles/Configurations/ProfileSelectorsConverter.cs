using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;

namespace Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Configurations;

/// <summary>Stores the cohort selectors as a JSONB key/value document.</summary>
/// <remarks>
/// The variable cohort dimensions differ by class year and evolve without a schema
/// migration, so a fixed set of columns would fight the model. A JSONB document
/// keeps them flexible while the relational academic-year/class-year/language
/// columns carry every audience query (systemPatterns §22).
/// </remarks>
internal sealed class ProfileSelectorsConverter()
    : ValueConverter<IReadOnlyDictionary<string, string>, string>(
        selectors => JsonSerializer.Serialize(selectors, SerializerOptions),
        json => Deserialize(json))
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();

    private static IReadOnlyDictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);
}
