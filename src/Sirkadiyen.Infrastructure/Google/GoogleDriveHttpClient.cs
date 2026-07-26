using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sirkadiyen.Application.ScheduleIngestion;

namespace Sirkadiyen.Infrastructure.Google;

/// <summary>
/// Reads a source document out of Google Drive over the Drive v3 REST API.
/// </summary>
/// <remarks>
/// <para>
/// Two calls, in this order. The metadata call answers what the file is, and the
/// media call downloads it. Doing the metadata call first is not a courtesy: a
/// Google-native document cannot be downloaded at all, a trashed one is no longer
/// a published source, and the stated length and digest are the only independent
/// way to tell a complete document from a truncated one. Deciding all of that
/// before reading a byte keeps a bad acquisition from becoming a bad snapshot.
/// </para>
/// <para>
/// The authorization header is attached by a delegating handler rather than here,
/// so the token never passes through this class and cannot reach a log line
/// (ADR-083).
/// </para>
/// </remarks>
public sealed class GoogleDriveHttpClient(HttpClient httpClient) : IGoogleDriveFileClient
{
    /// <summary>The Drive v3 REST base address.</summary>
    public const string BaseAddress = "https://www.googleapis.com/drive/v3/";

    /// <summary>
    /// Exactly the metadata this client acts on. Drive returns a minimal
    /// projection unless asked, and asking for everything would pull the sharing
    /// and owner information the pipeline has no business reading.
    /// </summary>
    private const string MetadataFields = "id,name,mimeType,size,md5Checksum,modifiedTime,trashed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DriveFile> FetchAsync(
        DriveFileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedMimeType);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumBytes, 1);

        DriveFileMetadata metadata = await ReadMetadataAsync(request.FileId, cancellationToken);
        Validate(request, metadata);

        byte[] content = await DownloadAsync(request, cancellationToken);
        VerifyContent(request.FileId, metadata, content);

        return new DriveFile
        {
            FileId = metadata.Id ?? request.FileId,
            Name = metadata.Name ?? string.Empty,
            MimeType = metadata.MimeType ?? string.Empty,
            Content = content,
            Md5Checksum = metadata.Md5Checksum,
            ModifiedAtUtc = ParseModifiedTime(metadata.ModifiedTime),
        };
    }

    private async Task<DriveFileMetadata> ReadMetadataAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"files/{Uri.EscapeDataString(fileId)}?fields={MetadataFields}&supportsAllDrives=true",
            cancellationToken);
        EnsureReadable(fileId, response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<DriveFileMetadata>(body, JsonOptions)
            ?? throw new InvalidDataException(
                $"Google Drive returned an empty metadata document for file '{fileId}'.");
    }

    private async Task<byte[]> DownloadAsync(
        DriveFileRequest request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"files/{Uri.EscapeDataString(request.FileId)}?alt=media&supportsAllDrives=true",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        EnsureReadable(request.FileId, response);

        // The declared length is checked before reading, and the bound is applied
        // again while reading: a response that states no length, or states one it
        // does not honour, must not be able to make the host read without limit.
        long? declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > request.MaximumBytes)
        {
            throw TooLarge(request, declaredLength.Value);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new(
            (int)Math.Min(declaredLength ?? 64 * 1024, request.MaximumBytes));
        byte[] chunk = new byte[81920];

        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > request.MaximumBytes)
            {
                throw TooLarge(request, buffer.Length + read);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void Validate(DriveFileRequest request, DriveFileMetadata metadata)
    {
        // A trashed file still downloads, and its content is whatever it held when
        // it was deleted. Reading it would let a document nobody publishes any
        // more keep feeding student calendars, so it is refused rather than
        // acquired with a warning.
        if (metadata.Trashed == true)
        {
            throw new DriveDocumentException(
                request.FileId,
                DriveDocumentFailure.Trashed,
                $"Google Drive file '{request.FileId}' is in the trash, so it is no longer a "
                + "published source. Restore it, or point the catalog entry at the file that "
                + "replaced it.");
        }

        if (!string.Equals(metadata.MimeType, request.ExpectedMimeType, StringComparison.Ordinal))
        {
            throw new DriveDocumentException(
                request.FileId,
                DriveDocumentFailure.UnexpectedFormat,
                $"Google Drive file '{request.FileId}' is a '{metadata.MimeType}' and the catalog "
                + $"declares '{request.ExpectedMimeType}'. A document converted into a Google "
                + "editor format cannot be downloaded and must be exported instead; a document "
                + "saved in another format needs a catalog change.");
        }

        if (TryParseSize(metadata.Size, out long size) && size > request.MaximumBytes)
        {
            throw TooLarge(request, size);
        }
    }

    /// <summary>
    /// Checks the downloaded bytes against what the metadata call stated they
    /// would be.
    /// </summary>
    /// <remarks>
    /// This is the only independent evidence that the download completed. A
    /// truncated DOCX usually fails to open, but one that opens with its last
    /// rows missing would convert into a schedule that is quietly short of
    /// lessons, and a diff would read that as deletions.
    /// </remarks>
    private static void VerifyContent(
        string fileId,
        DriveFileMetadata metadata,
        byte[] content)
    {
        if (TryParseSize(metadata.Size, out long size) && size != content.Length)
        {
            throw new DriveDocumentException(
                fileId,
                DriveDocumentFailure.CorruptContent,
                $"Google Drive stated {size.ToString(CultureInfo.InvariantCulture)} bytes for file "
                + $"'{fileId}' and the download produced "
                + $"{content.Length.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (string.IsNullOrWhiteSpace(metadata.Md5Checksum))
        {
            return;
        }

        // MD5 is Drive's own integrity digest, not a security control: it is
        // compared against a value the same response supplied, to detect a
        // truncated or corrupted transfer.
#pragma warning disable CA5351 // Not used for any cryptographic guarantee.
        string digest = Convert.ToHexStringLower(MD5.HashData(content));
#pragma warning restore CA5351
        if (!string.Equals(digest, metadata.Md5Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new DriveDocumentException(
                fileId,
                DriveDocumentFailure.CorruptContent,
                $"The download of Google Drive file '{fileId}' does not match the digest Drive "
                + "stated for it, so the bytes are not the document.");
        }
    }

    /// <summary>
    /// Translates the statuses that mean a person must act, and leaves every
    /// other failure as the transient HTTP error the next poll retries.
    /// </summary>
    /// <remarks>
    /// The response body is deliberately not read into the message. Google states
    /// its errors in a document that can name the file, its owner and the
    /// authenticated principal, and none of that belongs in a log line (ADR-015,
    /// guideline 15).
    /// </remarks>
    private static void EnsureReadable(string fileId, HttpResponseMessage response)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new DriveDocumentException(
                    fileId,
                    DriveDocumentFailure.NotFound,
                    $"Google Drive has no file '{fileId}' that this credential can see. It was "
                    + "moved, deleted, or never shared with the source account.");

            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                throw new DriveDocumentException(
                    fileId,
                    DriveDocumentFailure.AccessDenied,
                    $"Google Drive refused access to file '{fileId}'. Either the file is not "
                    + "shared with the configured source credential, or that credential was "
                    + "authorized without the Drive read-only scope.");

            default:
                response.EnsureSuccessStatusCode();
                break;
        }
    }

    private static DriveDocumentException TooLarge(DriveFileRequest request, long actualBytes) =>
        new(
            request.FileId,
            DriveDocumentFailure.TooLarge,
            $"Google Drive file '{request.FileId}' is "
            + $"{actualBytes.ToString(CultureInfo.InvariantCulture)} bytes, above the "
            + $"{request.MaximumBytes.ToString(CultureInfo.InvariantCulture)} byte bound one "
            + "acquisition may read into memory.");

    // Drive states a 64-bit length as a JSON string, and states none at all for a
    // file that has no binary content.
    private static bool TryParseSize(string? value, out long size)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return long.TryParse(value, CultureInfo.InvariantCulture, out size);
        }

        size = 0;
        return false;
    }

    private static DateTimeOffset? ParseModifiedTime(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;

    private sealed record DriveFileMetadata
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? MimeType { get; init; }

        public string? Size { get; init; }

        [JsonPropertyName("md5Checksum")]
        public string? Md5Checksum { get; init; }

        public string? ModifiedTime { get; init; }

        public bool? Trashed { get; init; }
    }
}
