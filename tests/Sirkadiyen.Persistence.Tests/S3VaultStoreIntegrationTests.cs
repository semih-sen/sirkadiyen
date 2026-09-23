using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Sirkadiyen.Application.Vault;
using Sirkadiyen.Infrastructure.Configuration;
using Sirkadiyen.Infrastructure.Vault;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// The vault store against a real S3-compatible server. Skipped unless the four
/// <c>SIRKADIYEN_TEST_VAULT__*</c> variables are set; each run works under a fresh key prefix and
/// deletes it afterwards, so the bucket may be shared, but it should still never be a real vault.
/// </summary>
public sealed class S3VaultStoreIntegrationTests : IAsyncLifetime
{
    private const string SkipReason =
        "Set SIRKADIYEN_TEST_VAULT__ENDPOINT, __ACCESS_KEY, __SECRET_KEY and __BUCKET to run the vault storage "
        + "integration tests. 'docker compose --profile vault up -d minio' starts a suitable server.";

    private VaultStorageOptions? options;
    private AmazonS3Client? client;
    private S3VaultStore? store;

    public async ValueTask InitializeAsync()
    {
        DotEnvFile.Load();
        string? endpoint = Environment.GetEnvironmentVariable("SIRKADIYEN_TEST_VAULT__ENDPOINT");
        string? accessKey = Environment.GetEnvironmentVariable("SIRKADIYEN_TEST_VAULT__ACCESS_KEY");
        string? secretKey = Environment.GetEnvironmentVariable("SIRKADIYEN_TEST_VAULT__SECRET_KEY");
        string? bucket = Environment.GetEnvironmentVariable("SIRKADIYEN_TEST_VAULT__BUCKET");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey)
            || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(bucket))
        {
            return;
        }

        options = new VaultStorageOptions
        {
            Endpoint = new Uri(endpoint),
            AccessKey = accessKey,
            SecretKey = secretKey,
            Bucket = bucket,
            Prefix = $"sirkadiyen-tests/{Guid.NewGuid():N}",
        };
        client = S3VaultStore.CreateClient(options);
        store = new S3VaultStore(client, options);
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(client, bucket))
        {
            await client.PutBucketAsync(bucket);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (client is null || options is null)
        {
            return;
        }

        ListObjectsV2Response listed = await client.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = options.Bucket, Prefix = options.NormalizedPrefix });
        foreach (S3Object item in listed.S3Objects ?? [])
        {
            await client.DeleteObjectAsync(options.Bucket, item.Key);
        }

        client.Dispose();
    }

    [Fact]
    public async Task Creates_reads_lists_and_replaces_under_the_prefix()
    {
        Assert.SkipUnless(store is not null, SkipReason);

        Assert.Equal(VaultWriteOutcome.Written, await store.CreateAsync("Kardiyoloji/Aritmi Tipleri.md", "# Aritmi — ğüşiöç\n", Token));
        Assert.Equal(VaultWriteOutcome.Written, await store.CreateAsync("Kök.md", "kök\n", Token));
        Assert.Equal(VaultWriteOutcome.Conflict, await store.CreateAsync("Kök.md", "ikinci\n", Token));

        Assert.Equal(["Kardiyoloji/Aritmi Tipleri.md", "Kök.md"], (await store.ListPathsAsync(Token)).Order(StringComparer.Ordinal));

        VaultDocument? read = await store.GetAsync("Kardiyoloji/Aritmi Tipleri.md", Token);
        Assert.NotNull(read);
        Assert.Equal("# Aritmi — ğüşiöç\n", read.Content);

        Assert.Equal(VaultWriteOutcome.Written, await store.ReplaceAsync(read.Path, read.Content + "[[Beta]]\n", read.ETag, Token));

        // The version read before that write is now stale.
        Assert.Equal(VaultWriteOutcome.Conflict, await store.ReplaceAsync(read.Path, "eski sürümden\n", read.ETag, Token));
        Assert.Equal("# Aritmi — ğüşiöç\n[[Beta]]\n", (await store.GetAsync(read.Path, Token))?.Content);
        Assert.Null(await store.GetAsync("Yok.md", Token));
    }

    /// <summary>
    /// Whether the server itself enforces conditional PUTs, bypassing the store's own pre-check. The
    /// store stays correct without it, but only this closes the gap between the check and the write;
    /// a failure here means the MinIO release predates conditional writes and should be upgraded.
    /// </summary>
    [Fact]
    public async Task Server_enforces_conditional_writes()
    {
        Assert.SkipUnless(client is not null && options is not null, SkipReason);
        string key = options.NormalizedPrefix + "koşullu.md";
        await client.PutObjectAsync(new PutObjectRequest { BucketName = options.Bucket, Key = key, ContentBody = "v1", UseChunkEncoding = false }, Token);

        AmazonS3Exception createOver = await Assert.ThrowsAsync<AmazonS3Exception>(() => client.PutObjectAsync(
            new PutObjectRequest { BucketName = options.Bucket, Key = key, ContentBody = "v2", IfNoneMatch = "*", UseChunkEncoding = false }, Token));
        AmazonS3Exception staleReplace = await Assert.ThrowsAsync<AmazonS3Exception>(() => client.PutObjectAsync(
            new PutObjectRequest { BucketName = options.Bucket, Key = key, ContentBody = "v2", IfMatch = "\"00000000000000000000000000000000\"", UseChunkEncoding = false }, Token));

        Assert.Equal(HttpStatusCode.PreconditionFailed, createOver.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleReplace.StatusCode);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
