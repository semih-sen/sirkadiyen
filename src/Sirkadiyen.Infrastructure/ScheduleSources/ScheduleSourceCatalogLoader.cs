using System.Text.Json;
using System.Text.Json.Serialization;
using Sirkadiyen.Application.ScheduleSources;

namespace Sirkadiyen.Infrastructure.ScheduleSources;

public sealed class ScheduleSourceCatalogLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<ScheduleSourceCatalog> LoadAsync(
        string catalogPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        await using FileStream stream = File.OpenRead(catalogPath);
        ScheduleSourceCatalog? catalog = await JsonSerializer.DeserializeAsync<ScheduleSourceCatalog>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (catalog is null)
        {
            throw new InvalidDataException("The schedule source catalog is empty.");
        }

        Validate(catalog);
        return catalog;
    }

    private static void Validate(ScheduleSourceCatalog catalog)
    {
        if (!string.Equals(catalog.CatalogVersion, "1.0", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported schedule source catalog version '{catalog.CatalogVersion}'.");
        }

        HashSet<string> sourceIds = new(StringComparer.Ordinal);
        foreach (ScheduleSourceDefinition source in catalog.Sources)
        {
            ValidateRequiredText(source.SourceId, nameof(source.SourceId));
            ValidateRequiredText(source.DisplayName, nameof(source.DisplayName));
            ValidateRequiredText(source.ParserProfile, nameof(source.ParserProfile));
            ValidateRequiredText(source.ParserProfileVersion, nameof(source.ParserProfileVersion));

            if (!sourceIds.Add(source.SourceId))
            {
                throw new InvalidDataException($"Duplicate source ID '{source.SourceId}'.");
            }

            if (!source.SourceUri.IsAbsoluteUri || source.SourceUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' must use an absolute HTTPS URI.");
            }

            if (source.Transport is ScheduleSourceTransport.GoogleSheets)
            {
                ValidateGoogleSheet(source);
            }
            else if (string.IsNullOrWhiteSpace(source.ExternalId)
                && source.Transport is ScheduleSourceTransport.GoogleDriveFile)
            {
                throw new InvalidDataException(
                    $"Google Drive source '{source.SourceId}' requires an external file ID.");
            }

            if (source.FixturePath is { } fixturePath && Path.IsPathRooted(fixturePath))
            {
                throw new InvalidDataException(
                    $"Fixture path for source '{source.SourceId}' must be repository-relative.");
            }
        }
    }

    private static void ValidateGoogleSheet(ScheduleSourceDefinition source)
    {
        if (source.DocumentFormat is not ScheduleDocumentFormat.GoogleSheet
            || string.IsNullOrWhiteSpace(source.ExternalId)
            || source.SheetGid is null or < 0)
        {
            throw new InvalidDataException(
                $"Google Sheets source '{source.SourceId}' requires a spreadsheet ID, gid, "
                + "and googleSheet document format.");
        }
    }

    private static void ValidateRequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Schedule source field '{fieldName}' is required.");
        }
    }
}
