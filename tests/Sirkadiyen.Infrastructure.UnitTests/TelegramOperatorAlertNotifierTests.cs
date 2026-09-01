using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Infrastructure.Notifications;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The transport half of ADR-144: what is put on the wire, and what happens when it fails.
/// </summary>
public sealed class TelegramOperatorAlertNotifierTests
{
    private const string BotToken = "1234567:secret-bot-token";

    [Fact]
    public async Task EveryConfiguredChatGetsItsOwnRequest()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Notifier(handler, [5027475773, 1176903009])
            .SendAsync(Alert(), CancellationToken.None);

        Assert.Equal(2, handler.Bodies.Count);
        Assert.Equal(
            ["5027475773", "1176903009"],
            handler.Bodies.Select(body => Read(body, "chat_id")));
        // A bot token is "<digits>:<secret>". A relative request path that does not start with a
        // slash makes the first segment look like a URI scheme, which silently sends the alert to
        // the wrong path.
        Assert.All(
            handler.Requests,
            uri => Assert.Equal($"/bot{BotToken}/sendMessage", uri));
    }

    [Fact]
    public async Task TheMessageIsSentAsTelegramHtmlWithoutALinkPreview()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Notifier(handler, [1]).SendAsync(Alert(), CancellationToken.None);

        string body = handler.Bodies[0];
        Assert.Equal("HTML", Read(body, "parse_mode"));
        Assert.True(
            JsonDocument.Parse(body).RootElement
                .GetProperty("disable_web_page_preview").GetBoolean());
        Assert.Contains("Kaynak okunamad", Read(body, "text"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedMessageIsNotAnExceptionForTheStageThatRaisedIt()
    {
        // The whole point of an alert is to report trouble; becoming trouble would mean a
        // messaging outage could stop a published schedule from reaching a calendar.
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"description\":\"bot was blocked by the user\"}"),
        });

        await Notifier(handler, [1]).SendAsync(Alert(), CancellationToken.None);

        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task AnUnreachableApiIsNotAnExceptionEither()
    {
        StubHandler handler = new(_ => throw new HttpRequestException("no such host is known"));

        await Notifier(handler, [1]).SendAsync(Alert(), CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task OneUnreachableChatDoesNotSilenceTheOthers()
    {
        int attempt = 0;
        StubHandler handler = new(_ =>
            ++attempt == 1
                ? throw new HttpRequestException("connection reset")
                : new HttpResponseMessage(HttpStatusCode.OK));

        await Notifier(handler, [1, 2]).SendAsync(Alert(), CancellationToken.None);

        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task ACancelledSendIsSwallowedRatherThanThrownAtTheCaller()
    {
        // Stages call this from inside their own cycle handling; a shutdown mid-alert must not
        // surface as a failure of the work that had just succeeded.
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Notifier(handler, [1]).SendAsync(Alert(), cancellation.Token);
    }

    [Fact]
    public async Task NothingIsSentWhenNoChannelIsConfigured()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Notifier(handler, [], token: null).SendAsync(Alert(), CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void TheBotTokenIsRemovedFromAnythingAboutToBeLogged()
    {
        // The token is a path segment, so an HTTP stack is entitled to put it in an error
        // message. This is the last line of defence behind removing the client's own logging.
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));

        string redacted = Notifier(handler, [1])
            .Redact($"An error occurred while sending to https://api.telegram.org/bot{BotToken}/sendMessage");

        Assert.DoesNotContain(BotToken, redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
    }

    private static TelegramOperatorAlertNotifier Notifier(
        StubHandler handler,
        IReadOnlyList<long> chatIds,
        string? token = BotToken) =>
        new(
            new StubHttpClientFactory(handler),
            new TelegramAlertOptions { BotToken = token, ChatIds = chatIds },
            NullLogger<TelegramOperatorAlertNotifier>.Instance);

    private static OperatorAlert Alert() => new()
    {
        Title = "Kaynak okunamadı",
        Severity = OperatorAlertSeverity.Error,
        DedupeKey = "source-poll-failed:G1-TR-ANNUAL",
    };

    private static string Read(string body, string property) =>
        JsonDocument.Parse(body).RootElement.GetProperty(property).ToString();

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = TelegramOperatorAlertNotifier.BaseAddress,
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = respond(request);
            Requests.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return response;
        }
    }
}
