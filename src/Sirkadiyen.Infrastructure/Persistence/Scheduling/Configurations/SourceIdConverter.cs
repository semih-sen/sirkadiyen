using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Configurations;

/// <summary>Converts the <see cref="SourceId"/> domain type to a stored string.</summary>
internal sealed class SourceIdConverter() : ValueConverter<SourceId, string>(
    sourceId => sourceId.Value,
    value => SourceId.Parse(value));
