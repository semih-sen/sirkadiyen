using System.Text.Json;
using System.Text.Json.Serialization;
using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.ScheduleSources;

public sealed class ScheduleSourceCatalogLoader
{
    /// <summary>
    /// How an administratively uploaded source names itself, since it has no
    /// location to be named by (ADR-079).
    /// </summary>
    public const string AdministrativeUploadUriPrefix = "urn:sirkadiyen:upload:";

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
            ValidateRequiredText(source.AcademicYear, nameof(source.AcademicYear));
            ValidateRequiredText(source.TimeZoneId, nameof(source.TimeZoneId));

            if (source.ClassYear is < 1 or > 6)
            {
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' states an unsupported class year "
                    + $"{source.ClassYear}.");
            }

            if (!sourceIds.Add(source.SourceId))
            {
                throw new InvalidDataException($"Duplicate source ID '{source.SourceId}'.");
            }

            ValidateSourceUri(source);

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

            ValidateSupportedAudienceSelectors(source);
        }
    }

    /// <summary>
    /// Enforces that the source URI states what the transport can actually do.
    /// </summary>
    /// <remarks>
    /// A fetched source must name an absolute HTTPS location, because the worker
    /// reads it from there. An administratively uploaded document has no location
    /// at all, and giving it a plausible-looking URL would be a false provenance
    /// claim, so it names itself with a URN instead. The URN must spell out its
    /// own source ID: it is pure identity, and a copied entry that kept another
    /// source's URN would attach one document's evidence to the other (ADR-079).
    /// </remarks>
    private static void ValidateSourceUri(ScheduleSourceDefinition source)
    {
        if (source.Transport is ScheduleSourceTransport.AdministrativeUpload)
        {
            string expected = AdministrativeUploadUriPrefix + source.SourceId;
            if (!string.Equals(
                source.SourceUri.OriginalString,
                expected,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Administratively uploaded source '{source.SourceId}' must identify itself "
                    + $"as '{expected}'.");
            }

            return;
        }

        if (!source.SourceUri.IsAbsoluteUri || source.SourceUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException(
                $"Source '{source.SourceId}' must use an absolute HTTPS URI.");
        }
    }

    /// <summary>
    /// Rejects a declared selector list that cannot mean what it appears to say.
    /// </summary>
    /// <remarks>
    /// A blank dimension or value, or the same value twice, would make the
    /// unknown-selector rule behave unpredictably rather than fail loudly, so the
    /// catalog refuses to load instead.
    /// </remarks>
    private static void ValidateSupportedAudienceSelectors(ScheduleSourceDefinition source)
    {
        if (source.SupportedAudienceSelectors is not { } declared)
        {
            return;
        }

        foreach ((string dimension, IReadOnlyList<string> values) in declared)
        {
            if (string.IsNullOrWhiteSpace(dimension))
            {
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' declares a supported selector dimension "
                    + "with no name.");
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' declares a blank supported selector "
                        + $"value for dimension '{dimension}'.");
                }

                if (!seen.Add(value))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' declares supported selector value "
                        + $"'{value}' twice for dimension '{dimension}'.");
                }
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
