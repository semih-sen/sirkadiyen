using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Infrastructure.Vault;

/// <summary>
/// The vault in an S3-compatible bucket. Writes are conditional - <c>If-None-Match: *</c> to create,
/// <c>If-Match</c> to replace - so a note edited in Obsidian after the job read it is never overwritten.
/// </summary>
/// <remarks>
/// MinIO honours conditional PUTs only from its 2024 releases on; an older server silently ignores
/// the headers and writes anyway. Each write is therefore preceded by a metadata check that refuses
/// on its own, leaving only the moment between the check and the write unguarded on such a server.
/// </remarks>
public sealed class S3VaultStore(IAmazonS3 client, VaultStorageOptions options) : IVaultStore
{
    private const string MarkdownContentType = "text/markdown; charset=utf-8";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string prefix = options.NormalizedPrefix;

    /// <summary>A client configured for MinIO rather than AWS.</summary>
    public static AmazonS3Client CreateClient(VaultStorageOptions options, HttpClientFactory? httpClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        AmazonS3Config config = new()
        {
            ServiceURL = options.Endpoint.ToString(),
            AuthenticationRegion = options.Region,

            // MinIO serves buckets by path; virtual-host addressing would need wildcard DNS for it.
            ForcePathStyle = true,

            // The SDK's default adds CRC trailers in aws-chunked uploads, which MinIO releases before
            // late 2024 reject. A checksum only when an operation requires one works on every release.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (httpClientFactory is not null)
        {
            config.HttpClientFactory = httpClientFactory;
        }

        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    public async Task<IReadOnlyList<string>> ListPathsAsync(CancellationToken cancellationToken)
    {
        List<string> paths = [];
        IListObjectsV2Paginator pages = client.Paginators.ListObjectsV2(new ListObjectsV2Request
        {
            BucketName = options.Bucket,
            Prefix = prefix.Length > 0 ? prefix : null,
        });

        await foreach (S3Object item in pages.S3Objects.WithCancellation(cancellationToken))
        {
            // A key ending in a slash is a folder marker some clients create, not a note.
            if (item.Key.EndsWith('/') || !item.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            paths.Add(item.Key[prefix.Length..]);
        }

        return paths;
    }

    public async Task<VaultDocument?> GetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using GetObjectResponse response = await client.GetObjectAsync(
                new GetObjectRequest { BucketName = options.Bucket, Key = KeyOf(path) },
                cancellationToken);
            using StreamReader reader = new(response.ResponseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string content = await reader.ReadToEndAsync(cancellationToken);
            return new VaultDocument(path, content, response.ETag);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<VaultWriteOutcome> CreateAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (await GetETagAsync(path, cancellationToken) is not null)
        {
            return VaultWriteOutcome.Conflict;
        }

        return await PutAsync(path, content, request => request.IfNoneMatch = "*", cancellationToken);
    }

    public async Task<VaultWriteOutcome> ReplaceAsync(
        string path,
        string content,
        string expectedETag,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);

        string? current = await GetETagAsync(path, cancellationToken);
        if (current is null || !SameETag(current, expectedETag))
        {
            return VaultWriteOutcome.Conflict;
        }

        return await PutAsync(path, content, request => request.IfMatch = expectedETag, cancellationToken);
    }

    private async Task<VaultWriteOutcome> PutAsync(
        string path,
        string content,
        Action<PutObjectRequest> condition,
        CancellationToken cancellationToken)
    {
        using MemoryStream body = new(Utf8WithoutBom.GetBytes(content));
        PutObjectRequest request = new()
        {
            BucketName = options.Bucket,
            Key = KeyOf(path),
            InputStream = body,
            ContentType = MarkdownContentType,

            // Over plain HTTP the SDK would otherwise sign the body in aws-chunked pieces. A note is a
            // few kilobytes, and one signed payload is the form every S3-compatible server accepts.
            UseChunkEncoding = false,
        };
        condition(request);

        try
        {
            await client.PutObjectAsync(request, cancellationToken);
            return VaultWriteOutcome.Written;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            // 412 is the condition failing; 409 is S3's answer to a concurrent conditional write.
            return VaultWriteOutcome.Conflict;
        }
    }

    private async Task<string?> GetETagAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            GetObjectMetadataResponse metadata = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = options.Bucket, Key = KeyOf(path) },
                cancellationToken);
            return metadata.ETag;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private string KeyOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return prefix + path;
    }

    /// <summary>ETags travel quoted in headers but not always in parsed responses; the quotes are not part of the value.</summary>
    private static bool SameETag(string left, string right) =>
        string.Equals(left.Trim('"'), right.Trim('"'), StringComparison.Ordinal);
}
