using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence.Configurations;

/// <summary>Converts the <see cref="SourceId"/> domain type to a stored string.</summary>
internal sealed class SourceIdConverter() : ValueConverter<SourceId, string>(
    sourceId => sourceId.Value,
    value => SourceId.Parse(value));
