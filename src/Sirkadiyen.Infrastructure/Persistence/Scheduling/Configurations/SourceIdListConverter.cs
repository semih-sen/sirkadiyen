using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

/// <summary>Stores an ordered list of source identifiers as a JSONB string array.</summary>
/// <remarks>
/// The companion list is short, ordered, and only ever read with the source row,
/// so a join table would buy nothing. Storing the identifiers as plain strings
/// rather than as foreign keys is deliberate: the catalog is the authority on
/// which sources exist, and the loader refuses a companion it cannot resolve
/// there, so a database-level reference would duplicate that check while making
/// catalog reordering a schema concern.
/// </remarks>
internal sealed class SourceIdListConverter()
    : ValueConverter<IReadOnlyList<SourceId>, string>(
        sourceIds => JsonSerializer.Serialize(
            sourceIds.Select(sourceId => sourceId.Value).ToList(),
            SerializerOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, SerializerOptions)!
            .Select(SourceId.Parse)
            .ToList())
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();
}
