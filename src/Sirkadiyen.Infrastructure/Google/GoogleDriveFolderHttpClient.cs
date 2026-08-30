using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sirkadiyen.Application.Scheduling.Ingestion;

namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// Lists a Drive folder over the Drive v3 REST API (ADR-133).
/// </summary>
/// <remarks>
/// <para>
/// The query filters on the parent folder, the MIME type and the trash flag, so
/// the caller never has to reject a candidate the source could not have been. A
/// trashed document is excluded rather than reported: the faculty deletes last
/// week's workbook, and a document in the owner's trash is not published.
/// </para>
/// <para>
/// Only one page is read. The bound is what stops a folder that has accumulated
/// years of documents from turning one poll into an unbounded walk; a folder that
/// holds more than the bound still resolves, because the query orders by
/// modification time and the document being looked for is the most recent one.
/// </para>
/// </remarks>
public sealed class GoogleDriveFolderHttpClient(HttpClient httpClient) : IGoogleDriveFolderClient
{
    /// <summary>The Drive v3 REST base address.</summary>
    public const string BaseAddress = GoogleDriveHttpClient.BaseAddress;

    /// <summary>
    /// Exactly the metadata this client acts on, for the reason the file client
    /// gives: asking for everything would read sharing and owner information the
    /// pipeline has no business seeing.
    /// </summary>
    private const string ListFields = "files(id,name,mimeType,modifiedTime)";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DriveFolderEntry>> ListAsync(
        DriveFolderListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FolderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedMimeType);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumEntries, 1);

        string query = $"'{Escape(request.FolderId)}' in parents"
            + $" and mimeType = '{Escape(request.ExpectedMimeType)}'"
            + " and trashed = false";

        string uri = "files?q=" + Uri.EscapeDataString(query)
            + "&fields=" + Uri.EscapeDataString(ListFields)
            + "&orderBy=" + Uri.EscapeDataString("modifiedTime desc")
            + "&pageSize=" + request.MaximumEntries.ToString(CultureInfo.InvariantCulture)
            + "&supportsAllDrives=true&includeItemsFromAllDrives=true";

        using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken);
        EnsureReadable(request.FolderId, response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        DriveFileListResponse? listing = JsonSerializer.Deserialize<DriveFileListResponse>(
            body,
            JsonOptions);

        if (listing?.Files is null)
        {
            throw new InvalidDataException(
                $"Google Drive returned an empty listing document for folder '{request.FolderId}'.");
        }

        List<DriveFolderEntry> entries = new(listing.Files.Count);
        foreach (DriveFileListEntry file in listing.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Id))
            {
                continue;
            }

            entries.Add(new DriveFolderEntry
            {
                FileId = file.Id,
                Name = file.Name ?? string.Empty,
                MimeType = file.MimeType ?? string.Empty,
                ModifiedAtUtc = ParseModifiedTime(file.ModifiedTime),
            });
        }

        return entries;
    }

    /// <summary>
    /// Escapes a value for a Drive query string literal.
    /// </summary>
    /// <remarks>
    /// A folder ID is opaque and a MIME type is configuration, but both are
    /// interpolated into a query language, and treating either as trusted because
    /// of where it came from is how injection is arrived at. Drive's grammar
    /// escapes a quote and a backslash with a backslash.
    /// </remarks>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

    private static DateTimeOffset ParseModifiedTime(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
            ? parsed
            // A document Drive states no modification time for sorts oldest, so it
            // can never be taken over one whose time is known.
            : DateTimeOffset.MinValue;

    private static void EnsureReadable(string folderId, HttpResponseMessage response)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new DriveDocumentException(
                    folderId,
                    DriveDocumentFailure.NotFound,
                    $"Google Drive has no folder '{folderId}' that this credential can see. It "
                    + "was moved, deleted, or never shared with the source account.");

            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                throw new DriveDocumentException(
                    folderId,
                    DriveDocumentFailure.AccessDenied,
                    $"Google Drive refused to list folder '{folderId}'. Either the folder is not "
                    + "shared with the configured source credential, or that credential was "
                    + "authorized without the Drive read-only scope.");

            default:
                response.EnsureSuccessStatusCode();
                break;
        }
    }

    private sealed record DriveFileListResponse
    {
        [JsonPropertyName("files")]
        public IReadOnlyList<DriveFileListEntry>? Files { get; init; }
    }

    private sealed record DriveFileListEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; init; }

        [JsonPropertyName("modifiedTime")]
        public string? ModifiedTime { get; init; }
    }
}
