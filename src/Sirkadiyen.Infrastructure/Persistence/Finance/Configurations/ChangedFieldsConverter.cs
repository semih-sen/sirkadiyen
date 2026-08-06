using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Contracts.Serialization;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Configurations;

/// <summary>Stores <see cref="Sirkadiyen.Domain.Finance.FinanceAudit.ChangedFields"/> as a JSONB string array.</summary>
internal sealed class ChangedFieldsConverter()
    : ValueConverter<IReadOnlyList<string>, string>(
        fields => JsonSerializer.Serialize(fields, SerializerOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, SerializerOptions)!)
{
    private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateOptions();
}
