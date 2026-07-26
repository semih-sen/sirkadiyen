using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Infrastructure.Google;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class GoogleDriveHttpClientTests
{
    private const string DocxMimeType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string GoogleDocMimeType = "application/vnd.google-apps.document";

    private static readonly byte[] Document = [0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03];

    [Fact]
    public async Task FetchReturnsTheDocumentAndWhatDriveStatesAboutIt()
    {
        StubHandler handler = Handler(Metadata(size: Document.Length, md5: Digest(Document)));

        DriveFile file = await Client(handler).FetchAsync(Request(), CancellationToken.None);

        Assert.Equal(Document, file.Content.ToArray());
        Assert.Equal("Dönem 2 Beceri uygulama takvimi güz.docx", file.Name);
        Assert.Equal(DocxMimeType, file.MimeType);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero),
            file.ModifiedAtUtc);

        // Metadata first, then the media download: what the file is decides
        // whether it is read at all.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("fields=", handler.Requests[0], StringComparison.Ordinal);
        Assert.Contains("supportsAllDrives=true", handler.Requests[0], StringComparison.Ordinal);
        Assert.Contains("alt=media", handler.Requests[1], StringComparison.Ordinal);

        // Only the fields this client acts on. The catalog's documents live in
        // other people's Drives, and sharing and ownership are none of the
        // pipeline's business.
        Assert.DoesNotContain("permissions", handler.Requests[0], StringComparison.Ordinal);
        Assert.DoesNotContain("owners", handler.Requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADocumentConvertedIntoAGoogleDocIsRefusedBeforeAnythingIsDownloaded()
    {
        StubHandler handler = Handler(Metadata(mimeType: GoogleDocMimeType));

        DriveDocumentException exception =
            await Assert.ThrowsAsync<DriveDocumentException>(
                () => Client(handler).FetchAsync(Request(), CancellationToken.None));

        Assert.Equal(DriveDocumentFailure.UnexpectedFormat, exception.Failure);

        // Downloading a Google-native document is not possible, and the message
        // has to say why rather than let a 403 look like a permission problem.
        Assert.Single(handler.Requests);
        Assert.Contains("exported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATrashedDocumentIsRefusedEvenThoughItWouldStillDownload()
    {
        StubHandler handler = Handler(Metadata(size: Document.Length, trashed: true));

        DriveDocumentException exception =
            await Assert.ThrowsAsync<DriveDocumentException>(
                () => Client(handler).FetchAsync(Request(), CancellationToken.None));

        // Drive serves a trashed file's last content indefinitely. Reading it
        // would let a document nobody publishes any more keep feeding calendars.
        Assert.Equal(DriveDocumentFailure.Trashed, exception.Failure);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, DriveDocumentFailure.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, DriveDocumentFailure.AccessDenied)]
    [InlineData(HttpStatusCode.Unauthorized, DriveDocumentFailure.AccessDenied)]
    public async Task AStatusThatNeedsAPersonIsNotLeftAsATransientError(
        HttpStatusCode status,
        DriveDocumentFailure expected)
    {
        StubHandler handler = new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"error\":{\"message\":\"denied\"}}"),
        });

        DriveDocumentException exception =
            await Assert.ThrowsAsync<DriveDocumentException>(
                () => Client(handler).FetchAsync(Request(), CancellationToken.None));

        Assert.Equal(expected, exception.Failure);
        Assert.Equal("file-1", exception.FileId);

        // Google states its errors in a document that can name the file, its
        // owner and the authenticated principal. None of that is repeated here.
        Assert.DoesNotContain("denied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATransientFailureStaysAnOrdinaryHttpErrorForTheNextPollToRetry()
    {
        StubHandler handler = new(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Client(handler).FetchAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task ATruncatedDownloadIsRefusedAgainstTheLengthDriveStated()
    {
        StubHandler handler = Handler(
            Metadata(size: Document.Length),
            content: Document[..3]);

        DriveDocumentException exception =
            await Assert.ThrowsAsync<DriveDocumentException>(
                () => Client(handler).FetchAsync(Request(), CancellationToken.None));

        // A DOCX missing its tail usually fails to open, but one that opens with
        // its last rows gone would convert into a schedule quietly short of
        // lessons, and the diff would read that as deletions.
        Assert.Equal(DriveDocumentFailure.CorruptContent, exception.Failure);
    }

    [Fact]
    public async Task ContentThatDoesNotMatchTheStatedDigestIsRefused()
    {
        StubHandler handler = Handler(
            Metadata(size: Document.Length, md5: Digest([.. Document, 0x09])));

        DriveDocumentException exception =
            await Assert.ThrowsAsync<DriveDocumentException>(
                () => Client(handler).FetchAsync(Request(), CancellationToken.None));

        Assert.Equal(DriveDocumentFailure.CorruptContent, exception.Failure);
    }

    [Fact]
    public async Task AFileLargerThanTheBoundIsRefusedFromItsStatedSize()
    {
        StubHandler handler = Handler(Metadata(size: 32 * 1024 * 1024));

        DriveDocumentException exception =
            await Assert.ThrowsAsync<DriveDocumentException>(
                () => Client(handler).FetchAsync(Request(), CancellationToken.None));

        Assert.Equal(DriveDocumentFailure.TooLarge, exception.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AResponseThatDeclaresNoLengthIsStillBoundedWhileItIsRead()
    {
        // Neither the metadata nor the response states a length, so the only
        // thing standing between the host and an unbounded read is the bound
        // applied to every chunk.
        StubHandler handler = new(request =>
            request.RequestUri!.Query.Contains("alt=media", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new EndlessStream()),
                }
                : Json(Metadata()));

        DriveDocumentException exception =
            await Assert.ThrowsAsync<DriveDocumentException>(
                () => Client(handler).FetchAsync(
                    Request() with { MaximumBytes = 64 * 1024 },
                    CancellationToken.None));

        Assert.Equal(DriveDocumentFailure.TooLarge, exception.Failure);
    }

    private static DriveFileRequest Request() => new()
    {
        FileId = "file-1",
        ExpectedMimeType = DocxMimeType,
        MaximumBytes = 8 * 1024 * 1024,
    };

    private static GoogleDriveHttpClient Client(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(GoogleDriveHttpClient.BaseAddress) });

    private static StubHandler Handler(string metadata, byte[]? content = null) =>
        new(request => request.RequestUri!.Query.Contains("alt=media", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content ?? Document),
            }
            : Json(metadata));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static string Metadata(
        string mimeType = DocxMimeType,
        long? size = null,
        string? md5 = null,
        bool trashed = false)
    {
        string sizeField = size is null
            ? string.Empty
            : $"\"size\":\"{size.Value.ToString(CultureInfo.InvariantCulture)}\",";
        string md5Field = md5 is null ? string.Empty : $"\"md5Checksum\":\"{md5}\",";

        return $$"""
            {
              "id": "file-1",
              "name": "Dönem 2 Beceri uygulama takvimi güz.docx",
              "mimeType": "{{mimeType}}",
              {{sizeField}}{{md5Field}}
              "modifiedTime": "2026-07-20T08:30:00.000Z",
              "trashed": {{(trashed ? "true" : "false")}}
            }
            """;
    }

    // Drive's own integrity digest, reproduced here to build the metadata a real
    // response would carry. Not a security control on either side.
#pragma warning disable CA5351
    private static string Digest(byte[] content) =>
        Convert.ToHexStringLower(MD5.HashData(content));
#pragma warning restore CA5351

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A response body that never ends and never states its length.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => count;

        public override int Read(Span<byte> buffer) => buffer.Length;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
