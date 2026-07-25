using System.Security.Cryptography;
using System.Text.Json;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Infrastructure.ScheduleIngestion;
using Sirkadiyen.Infrastructure.ScheduleSources;

Dictionary<string, string> arguments = ParseArguments(args);
string repositoryRoot = GetRequired(arguments, "repository-root");
string sourceId = GetRequired(arguments, "source-id");
string outputPath = GetRequired(arguments, "output");
DateTimeOffset acquiredAtUtc = DateTimeOffset.Parse(
    GetRequired(arguments, "acquired-at-utc"),
    System.Globalization.CultureInfo.InvariantCulture,
    System.Globalization.DateTimeStyles.AssumeUniversal
        | System.Globalization.DateTimeStyles.AdjustToUniversal);

if (acquiredAtUtc.Offset != TimeSpan.Zero)
{
    throw new ArgumentException("--acquired-at-utc must resolve to UTC.");
}

// A document may be collected before the catalog describes it, so --document
// converts a file under a source ID the manifest reserves but the catalog does
// not carry yet. The catalog stays the authority for every source it does carry,
// including the administratively uploaded ones (ADR-079).
string? explicitDocument = arguments.GetValueOrDefault("document");
string? externalId = null;
if (explicitDocument is null)
{
    string catalogPath = Path.Combine(repositoryRoot, "config", "schedule-sources.json");
    ScheduleSourceCatalog catalog = await new ScheduleSourceCatalogLoader()
        .LoadAsync(catalogPath, CancellationToken.None);
    ScheduleSourceDefinition source = catalog.Sources.SingleOrDefault(
        candidate => string.Equals(candidate.SourceId, sourceId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Source '{sourceId}' was not found in the catalog.");

    if (source.FixturePath is null)
    {
        throw new InvalidOperationException(
            $"Source '{source.SourceId}' has no local fixture. Pass --document to convert a file "
            + "the catalog does not describe.");
    }

    explicitDocument = source.FixturePath;
    externalId = source.ExternalId;
}

bool isXlsx = explicitDocument.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
bool isDocx = explicitDocument.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
if (!isXlsx && !isDocx)
{
    throw new InvalidOperationException(
        $"Source '{sourceId}' does not point at a local XLSX or DOCX document.");
}

string fixturePath = ResolveRepositoryPath(repositoryRoot, explicitDocument);
byte[] fixtureHash = SHA256.HashData(await File.ReadAllBytesAsync(fixturePath));
string fixtureDigest = Convert.ToHexStringLower(fixtureHash);
AcquireSpreadsheetSnapshotRequest request = new()
{
    SourceId = sourceId,
    SnapshotId = $"fixture:sha256:{fixtureDigest}",
    SpreadsheetId = externalId ?? $"fixture:sha256:{fixtureDigest}",
    AcquiredAtUtc = acquiredAtUtc,
};

// A Word document is converted onto the same normalized snapshot contract as a
// workbook (ADR-076), so only the reader differs here.
NormalizedSpreadsheetSnapshot snapshot = isXlsx
    ? new LocalXlsxSnapshotConverter().Convert(fixturePath, request)
    : new LocalDocxSnapshotConverter().Convert(fixturePath, request);
JsonSerializerOptions jsonOptions = ContractJson.CreateOptions();
string json = JsonSerializer.Serialize(snapshot, jsonOptions);

string resolvedOutput = ResolveRepositoryPath(repositoryRoot, outputPath);
Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutput)
    ?? throw new InvalidOperationException("Output path does not have a directory."));
await File.WriteAllTextAsync(resolvedOutput, json + Environment.NewLine);

Console.WriteLine(
    $"Wrote {snapshot.Worksheets.Count} worksheets and "
    + $"{snapshot.Worksheets.Sum(static worksheet => worksheet.Cells.Count)} cells "
    + $"for {sourceId} to {resolvedOutput}.");

static Dictionary<string, string> ParseArguments(string[] values)
{
    Dictionary<string, string> parsed = new(StringComparer.Ordinal);
    for (int index = 0; index < values.Length; index += 2)
    {
        if (index + 1 >= values.Length || !values[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Arguments must be supplied as --name value pairs.");
        }

        parsed.Add(values[index][2..], values[index + 1]);
    }

    return parsed;
}

static string GetRequired(IReadOnlyDictionary<string, string> values, string name) =>
    values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required --{name} argument.");

static string ResolveRepositoryPath(string repositoryRoot, string path)
{
    string root = Path.GetFullPath(repositoryRoot);
    string resolved = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
    string rootedPrefix = root.EndsWith(Path.DirectorySeparatorChar)
        ? root
        : root + Path.DirectorySeparatorChar;
    if (!resolved.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Resolved path escapes the repository root.");
    }

    return resolved;
}
