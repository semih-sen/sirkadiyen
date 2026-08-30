// Verifies that the configured Google source credential can actually reach a
// catalogued source's document — and, for a source whose document is republished
// weekly, that it can list the folder that document is published into (ADR-133).
//
// It exists because that last question cannot be answered from inside the
// repository. `drive.readonly` is in the credential's scope list and the folder is
// link-shared, so folder discovery is expected to work; whether *this* credential
// may list *that* folder is a fact about the deployment. Discovery deliberately
// never fails a cycle — a folder it cannot read falls back to the catalogued
// document — so a misconfiguration shows up as rooms quietly freezing rather than
// as an error. This runs the same adapters the worker runs, so a pass here is
// evidence about the real path rather than about an approximation of it.
//
// It reads and prints nothing secret: the credential is built by the production
// factory and the token never leaves the handler that attaches it.
using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Infrastructure.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Configuration;
using Sirkadiyen.Infrastructure.Google;
using Sirkadiyen.Infrastructure.Scheduling.Sources;

Dictionary<string, string> arguments = ParseArguments(args);
string sourceId = arguments.GetValueOrDefault("source-id", "SHARED-AMPHI");
string repositoryRoot = arguments.GetValueOrDefault("repository-root", Directory.GetCurrentDirectory());

DotEnvLoadResult env = DotEnvFile.Load(repositoryRoot);
Console.WriteLine(env.FilePath is null
    ? "No .env file was found; reading configuration from the environment alone."
    : $"Loaded {env.AppliedCount} setting(s) from {env.FilePath}.");

GoogleSourceAccessOptions options = new()
{
    ClientId = Read("CLIENT_ID"),
    ClientSecret = Read("CLIENT_SECRET"),
    SourceRefreshToken = Read("SOURCE_REFRESH_TOKEN"),
    ServiceAccountCredentialPath = Read("SERVICE_ACCOUNT_CREDENTIAL_PATH"),
};

bool serviceAccount = !string.IsNullOrWhiteSpace(options.ServiceAccountCredentialPath);
Console.WriteLine($"Credential mode: {(serviceAccount ? "service account" : "OAuth refresh token")}.");

if (serviceAccount)
{
    // Printed before the credential is built, and separately from it: this address
    // is what the folder has to be shared with and is the one thing an operator
    // cannot guess, so a credential file that turns out to be unusable must not
    // also hide who to share with.
    Console.WriteLine($"Service account: {ReadServiceAccountEmail(options.ServiceAccountCredentialPath!)}");
}

ICredential credential;
try
{
    credential = new GoogleSourceCredentialFactory().Create(options);
}
catch (Exception exception) when (exception is InvalidOperationException
    or ArgumentException
    or System.Text.Json.JsonException
    or IOException)
{
    // Translated rather than swallowed: a malformed key file is the operator's
    // problem to fix, and a stack trace through the Google library is not how to
    // tell them that.
    return Fail($"The source credential could not be built: {exception.Message}");
}

ScheduleSourceCatalog catalog = await new ScheduleSourceCatalogLoader().LoadAsync(
    Path.Combine(repositoryRoot, "config", "schedule-sources.json"),
    CancellationToken.None);

ScheduleSourceDefinition? definition = catalog.Sources.SingleOrDefault(
    candidate => string.Equals(candidate.SourceId, sourceId, StringComparison.Ordinal));
if (definition is null)
{
    return Fail($"Source '{sourceId}' is not in the catalog.");
}

ScheduleSource source = definition.ToScheduleSource();
Console.WriteLine($"Source: {source.SourceId} ({source.DisplayName})");

if (string.IsNullOrWhiteSpace(source.DiscoveryFolderId))
{
    Console.WriteLine(
        "This source declares no discovery folder, so it always acquires the catalogued "
        + $"document ({source.ExternalId}). Nothing to verify.");
    return 0;
}

Console.WriteLine($"Discovery folder: {source.DiscoveryFolderId}");
Console.WriteLine($"Catalogued fallback document: {source.ExternalId}");
Console.WriteLine();

using HttpClient httpClient = new(new GoogleSourceAccessTokenHandler(credential)
{
    InnerHandler = new HttpClientHandler(),
})
{
    BaseAddress = new Uri(GoogleDriveFolderHttpClient.BaseAddress),
    Timeout = TimeSpan.FromSeconds(30),
};

GoogleDriveFolderHttpClient folderClient = new(httpClient);

// The raw listing first, because its failure carries the diagnosis. Discovery
// swallows that failure by design, so asking it alone would report "fell back"
// without saying why.
try
{
    IReadOnlyList<DriveFolderEntry> entries = await folderClient.ListAsync(
        new DriveFolderListRequest
        {
            FolderId = source.DiscoveryFolderId,
            ExpectedMimeType = WeeklyDocumentDiscovery.GoogleSheetMimeType,
            MaximumEntries = WeeklyDocumentDiscovery.MaximumEntries,
        },
        CancellationToken.None);

    Console.WriteLine($"The folder listed successfully and holds {entries.Count} candidate(s):");
    foreach (DriveFolderEntry entry in entries.OrderByDescending(item => item.ModifiedAtUtc))
    {
        Console.WriteLine($"  {entry.ModifiedAtUtc:u}  {entry.FileId}  {entry.Name}");
    }

    if (entries.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine(
            "WARNING: the credential can see the folder but it holds no Google Sheets "
            + "document. Every cycle will fall back to the catalogued document, so newly "
            + "published workbooks will not be picked up.");
        return 2;
    }
}
catch (DriveDocumentException exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAILED to list the folder: {exception.Failure}");
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(exception.Failure switch
    {
        DriveDocumentFailure.NotFound =>
            "The credential cannot see this folder. Share the Drive folder with the "
            + "identity printed above, as Viewer.",
        DriveDocumentFailure.AccessDenied =>
            "The credential was refused. Either the folder is not shared with the identity "
            + "printed above, or the grant does not carry the drive.readonly scope. A refresh "
            + "token minted before Drive acquisition existed holds the Sheets scope alone and "
            + "must be re-issued.",
        _ => "See the failure above.",
    });
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        "Until this is fixed the worker keeps acquiring the catalogued document and logs a "
        + "warning every cycle. Rooms will freeze at that workbook rather than break.");
    return 1;
}

// Then the decision the poller would actually make with the same listing.
WeeklyDocumentResolution resolution = await new WeeklyDocumentDiscovery(folderClient)
    .ResolveAsync(source, CancellationToken.None);

Console.WriteLine();
Console.WriteLine($"The next poll would acquire: {resolution.DocumentName} ({resolution.ExternalId})");
Console.WriteLine($"Outcome: {resolution.Outcome}");

// Listing a folder and reading a document in it are separate permissions, so a
// folder that lists is not yet proof the workbook can be acquired. Reading it is
// what closes the chain the poller actually walks.
Console.WriteLine();
try
{
    using SheetsService sheetsService = new GoogleSheetsServiceFactory(
        new GoogleSourceCredentialFactory()).Create(options);

    NormalizedSpreadsheetSnapshot snapshot = await new GoogleSheetsSnapshotAcquirer(
        sheetsService,
        new GoogleSheetsSnapshotMapper()).AcquireAsync(
        new AcquireSpreadsheetSnapshotRequest
        {
            SourceId = source.SourceId.Value,
            SnapshotId = "source-access-check",
            SpreadsheetId = resolution.ExternalId,
            AcquiredAtUtc = DateTimeOffset.UtcNow,
        },
        CancellationToken.None);

    Console.WriteLine(
        $"The workbook was acquired: {snapshot.Worksheets.Count} worksheet(s), "
        + $"{snapshot.Worksheets.Sum(worksheet => worksheet.Cells.Count)} cell(s).");
    foreach (NormalizedWorksheet worksheet in snapshot.Worksheets)
    {
        Console.WriteLine($"  {worksheet.Title} ({worksheet.Cells.Count} cells)");
    }

    // A live acquisition is not the same shape as a converted local workbook — the
    // Sheets API reports cells the XLSX converter never emits — so being able to
    // write one out is what lets the parser be tested against what production
    // actually feeds it rather than against a converted approximation.
    if (arguments.GetValueOrDefault("write-snapshot") is { } snapshotPath)
    {
        await using FileStream output = File.Create(snapshotPath);
        await System.Text.Json.JsonSerializer.SerializeAsync(
            output,
            snapshot,
            Sirkadiyen.Contracts.Serialization.ContractJson.CreateOptions());
        Console.WriteLine($"Wrote the acquired snapshot to {snapshotPath}.");
    }
}
catch (Google.GoogleApiException exception)
{
    Console.Error.WriteLine($"FAILED to read the workbook: {exception.Message}");
    Console.Error.WriteLine(
        "The folder can be listed but the document it names cannot be opened. Share the "
        + "document itself with the identity printed above, or share the folder in a way "
        + "that grants its contents.");
    return 1;
}

return resolution.Outcome is WeeklyDocumentDiscoveryOutcome.FellBackToCatalog ? 2 : 0;

static string? Read(string key) =>
    Environment.GetEnvironmentVariable($"{GoogleSourceAccessOptions.ConfigurationSection}__{key}");

static string ReadServiceAccountEmail(string path)
{
    try
    {
        using FileStream stream = File.OpenRead(path);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(stream);
        return document.RootElement.TryGetProperty("client_email", out System.Text.Json.JsonElement email)
            ? email.GetString() ?? "(the credential file states no client_email)"
            : "(the credential file states no client_email)";
    }
    catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
    {
        return $"(could not be read: {exception.Message})";
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    Dictionary<string, string> parsed = new(StringComparer.Ordinal);
    for (int index = 0; index < args.Length - 1; index++)
    {
        if (args[index].StartsWith("--", StringComparison.Ordinal))
        {
            parsed[args[index][2..]] = args[index + 1];
        }
    }

    return parsed;
}
