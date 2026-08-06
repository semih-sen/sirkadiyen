using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

/// <summary>Stores the declared audience selectors as a JSONB document.</summary>
internal sealed class AudienceSelectorMapConverter()
    : ValueConverter<IReadOnlyDictionary<string, IReadOnlyList<string>>?, string?>(
        map => JsonSerializer.Serialize(map, SerializerOptions),
        json => JsonSerializer
            .Deserialize<Dictionary<string, IReadOnlyList<string>>>(json!, SerializerOptions)!)
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();
}
