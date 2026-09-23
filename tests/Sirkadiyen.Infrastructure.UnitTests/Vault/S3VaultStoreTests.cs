using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Sirkadiyen.Application.Vault;
using Sirkadiyen.Infrastructure.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

/// <summary>
/// Runs the real SDK against an in-process fake S3 endpoint, so what is pinned down is the HTTP the
/// store actually sends - the conditional headers above all - rather than calls on a mocked client.
/// </summary>
public sealed class S3VaultStoreTests : IDisposable
{
    private readonly FakeS3 s3 = new();
    private readonly AmazonS3Client client;
    private readonly S3VaultStore store;

    public S3VaultStoreTests()
    {
        VaultStorageOptions options = new()
        {
            Endpoint = new Uri("http://minio.test:9000"),
            AccessKey = "key",
            SecretKey = "secret",
            Bucket = "vault",
            Prefix = "/obsidian/",
        };
        client = S3VaultStore.CreateClient(options, new FakeHttpClientFactory(s3));
        store = new S3VaultStore(client, options);
    }

    public void Dispose() => client.Dispose();

    [Fact]
    public async Task Lists_every_page_under_the_prefix_without_folder_markers()
    {
        s3.ListPages.Add(["obsidian/Kardiyoloji/", "obsidian/Kardiyoloji/Aritmi.md"]);
        s3.ListPages.Add(["obsidian/Kök Not.md"]);

        IReadOnlyList<string> paths = await store.ListPathsAsync(CancellationToken.None);

        Assert.Equal(["Kardiyoloji/Aritmi.md", "Kök Not.md"], paths);
        Assert.All(s3.Requests.Where(static r => r.Method == "GET"), static r => Assert.Contains("prefix=obsidian%2F", r.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lists_an_empty_vault()
    {
        s3.ListPages.Add([]);

        Assert.Empty(await store.ListPathsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reads_a_note_with_its_etag()
    {
        s3.Objects["obsidian/Kardiyoloji/Aritmi.md"] = "# Aritmi — ğüşiöç\n";

        VaultDocument? document = await store.GetAsync("Kardiyoloji/Aritmi.md", CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal("# Aritmi — ğüşiöç\n", document.Content);
        Assert.Equal(FakeS3.ETagOf("# Aritmi — ğüşiöç\n"), document.ETag);
    }

    [Fact]
    public async Task Reads_a_missing_note_as_null() =>
        Assert.Null(await store.GetAsync("Yok.md", CancellationToken.None));

    [Fact]
    public async Task Creates_with_if_none_match()
    {
        VaultWriteOutcome outcome = await store.CreateAsync("Farmakoloji/Beta Blokerler.md", "# Beta\n", CancellationToken.None);

        Assert.Equal(VaultWriteOutcome.Written, outcome);
        FakeS3.Recorded put = Assert.Single(s3.Requests, static r => r.Method == "PUT");
        Assert.Equal("/vault/obsidian/Farmakoloji/Beta Blokerler.md", put.Path);
        Assert.Equal("*", put.Headers["If-None-Match"]);
        Assert.Equal("# Beta\n", put.Body);
        Assert.StartsWith("text/markdown", put.Headers["Content-Type"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_to_create_over_an_existing_note_without_writing()
    {
        s3.Objects["obsidian/Var.md"] = "eski";

        Assert.Equal(VaultWriteOutcome.Conflict, await store.CreateAsync("Var.md", "yeni", CancellationToken.None));
        Assert.DoesNotContain(s3.Requests, static r => r.Method == "PUT");
    }

    [Fact]
    public async Task Reports_a_create_the_server_refused_as_a_conflict()
    {
        s3.RefusePuts = true;

        Assert.Equal(VaultWriteOutcome.Conflict, await store.CreateAsync("Yeni.md", "x", CancellationToken.None));
    }

    [Fact]
    public async Task Replaces_with_if_match()
    {
        s3.Objects["obsidian/Not.md"] = "eski";
        string etag = FakeS3.ETagOf("eski");

        VaultWriteOutcome outcome = await store.ReplaceAsync("Not.md", "yeni", etag, CancellationToken.None);

        Assert.Equal(VaultWriteOutcome.Written, outcome);
        Assert.Equal(etag, Assert.Single(s3.Requests, static r => r.Method == "PUT").Headers["If-Match"]);
        Assert.Equal("yeni", s3.Objects["obsidian/Not.md"]);
    }

    [Fact]
    public async Task Refuses_to_replace_a_changed_note_without_writing()
    {
        s3.Objects["obsidian/Not.md"] = "Obsidian'da düzenlendi";

        Assert.Equal(VaultWriteOutcome.Conflict, await store.ReplaceAsync("Not.md", "yeni", FakeS3.ETagOf("eski"), CancellationToken.None));
        Assert.DoesNotContain(s3.Requests, static r => r.Method == "PUT");
    }

    [Fact]
    public async Task Refuses_to_replace_a_deleted_note() =>
        Assert.Equal(VaultWriteOutcome.Conflict, await store.ReplaceAsync("Yok.md", "yeni", "\"v1\"", CancellationToken.None));

    [Fact]
    public async Task Reports_a_replace_the_server_refused_as_a_conflict()
    {
        s3.Objects["obsidian/Not.md"] = "eski";
        s3.RefusePuts = true;

        // Unquoted, as a caller may hold it; the pre-check must still see the same version.
        string unquoted = FakeS3.ETagOf("eski").Trim('"');
        Assert.Equal(VaultWriteOutcome.Conflict, await store.ReplaceAsync("Not.md", "yeni", unquoted, CancellationToken.None));
        Assert.Contains(s3.Requests, static r => r.Method == "PUT");
        Assert.Equal("eski", s3.Objects["obsidian/Not.md"]);
    }

    private sealed class FakeHttpClientFactory(FakeS3 s3) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(s3, disposeHandler: false);

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;
    }

    private sealed class FakeS3 : HttpMessageHandler
    {
        public Dictionary<string, string> Objects { get; } = new(StringComparer.Ordinal);

        public List<string[]> ListPages { get; } = [];

        public bool RefusePuts { get; set; }

        /// <summary>
        /// As S3 and MinIO compute it for a single-part object - the quoted hex MD5 of the body - which
        /// the SDK checks the downloaded body against.
        /// </summary>
        public static string ETagOf(string content) =>
            $"\"{Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(content)))}\"";

        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            string query = request.RequestUri.Query;
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Dictionary<string, string> headers = request.Headers
                .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(static header => header.Key, static header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Add(new Recorded(request.Method.Method, path, query, headers, body));

            const string BucketPath = "/vault/";
            string key = path.StartsWith(BucketPath, StringComparison.Ordinal) ? path[BucketPath.Length..] : string.Empty;

            if (request.Method == HttpMethod.Get && query.Contains("list-type=2", StringComparison.Ordinal))
            {
                return List(query);
            }

            if (request.Method == HttpMethod.Put)
            {
                if (RefusePuts)
                {
                    return Error(HttpStatusCode.PreconditionFailed, "PreconditionFailed");
                }

                Objects[key] = body ?? string.Empty;
                HttpResponseMessage written = new(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
                written.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(ETagOf(Objects[key]));
                return written;
            }

            if (!Objects.TryGetValue(key, out var stored))
            {
                return request.Method == HttpMethod.Head
                    ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) }
                    : Error(HttpStatusCode.NotFound, "NoSuchKey");
            }

            HttpResponseMessage found = new(HttpStatusCode.OK)
            {
                Content = request.Method == HttpMethod.Head
                    ? new ByteArrayContent([])
                    : new ByteArrayContent(Encoding.UTF8.GetBytes(stored)),
            };
            found.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(ETagOf(stored));
            return found;
        }

        private HttpResponseMessage List(string query)
        {
            int page = query.Contains("continuation-token=page", StringComparison.Ordinal)
                ? int.Parse(query[(query.IndexOf("continuation-token=page", StringComparison.Ordinal) + "continuation-token=page".Length)..].Split('&')[0], System.Globalization.CultureInfo.InvariantCulture)
                : 0;
            string[] keys = ListPages.Count > page ? ListPages[page] : [];
            bool truncated = page + 1 < ListPages.Count;

            StringBuilder xml = new();
            xml.Append("""<?xml version="1.0" encoding="UTF-8"?><ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/"><Name>vault</Name>""");
            xml.Append($"<KeyCount>{keys.Length}</KeyCount><MaxKeys>1000</MaxKeys><IsTruncated>{(truncated ? "true" : "false")}</IsTruncated>");
            if (truncated)
            {
                xml.Append($"<NextContinuationToken>page{page + 1}</NextContinuationToken>");
            }

            foreach (string key in keys)
            {
                xml.Append($"<Contents><Key>{System.Security.SecurityElement.Escape(key)}</Key><ETag>\"x\"</ETag><Size>1</Size></Contents>");
            }

            xml.Append("</ListBucketResult>");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml.ToString(), Encoding.UTF8, "application/xml") };
        }

        private static HttpResponseMessage Error(HttpStatusCode status, string code) => new(status)
        {
            Content = new StringContent(
                $"""<?xml version="1.0" encoding="UTF-8"?><Error><Code>{code}</Code><Message>{code}</Message></Error>""",
                Encoding.UTF8,
                "application/xml"),
        };

        public sealed record Recorded(string Method, string Path, string Query, Dictionary<string, string> Headers, string? Body);
    }
}
