using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stores the department list as JSONB.
/// </summary>
/// <remarks>
/// A lesson names zero, one or several departments, and the order is the source's
/// order. A single text column would force the reader to re-split what the parser
/// already separated, and a child table would add a join for a value that is only
/// ever read with its record.
/// </remarks>
internal sealed class DepartmentListConverter()
    : ValueConverter<IReadOnlyList<string>, string>(
        departments => JsonSerializer.Serialize(departments, SerializerOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, SerializerOptions)!)
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();
}
