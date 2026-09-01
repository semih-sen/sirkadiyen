using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Notifications;

namespace Sirkadiyen.Infrastructure.Notifications;

/// <summary>
/// Sends operator alerts to Telegram chats through the Bot API (ADR-144).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is allowed to fail the caller.</b> Every send is wrapped, including
/// cancellation: a stage that has just published a revision must not have its cycle disturbed
/// because a messaging service was unreachable. A failure is logged once, at warning, and the
/// alert is lost — which is the correct trade, because everything an alert names is already
/// persisted and visible in the panel.
/// </para>
/// <para>
/// <b>The bot token is in the request path</b>, which is how the Bot API authenticates. That makes
/// the URL itself a credential: it is never logged, the client's own request logging is removed at
/// registration, and anything derived from an exception is redacted before it reaches a log line.
/// </para>
/// <para>
/// Each chat is sent its own request, because one unreachable recipient must not silence the
/// others.
/// </para>
/// </remarks>
public sealed class TelegramOperatorAlertNotifier(
    IHttpClientFactory httpClientFactory,
    TelegramAlertOptions options,
    ILogger<TelegramOperatorAlertNotifier> logger) : IOperatorAlertNotifier
{
    /// <summary>The named client this adapter resolves, registered with its logging removed.</summary>
    public const string HttpClientName = "sirkadiyen-telegram-alerts";

    private const string ApiBaseAddress = "https://api.telegram.org/";

    public static Uri BaseAddress => new(ApiBaseAddress);

    public async Task SendAsync(OperatorAlert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (!options.IsConfigured)
        {
            return;
        }

        string text = TelegramAlertMessageFormatter.Format(alert);
        foreach (long chatId in options.ChatIds)
        {
            await SendToChatAsync(chatId, text, cancellationToken);
        }
    }

    private async Task SendToChatAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                // Rooted at "/" deliberately. A bot token is "<digits>:<secret>", so without the
                // leading slash the leading segment parses as a URI scheme and the request goes
                // to a mangled path that Telegram answers with a 404.
                new Uri($"/bot{options.BotToken}/sendMessage", UriKind.Relative),
                new SendMessageRequest(
                    chatId.ToString(CultureInfo.InvariantCulture),
                    text,
                    "HTML",
                    DisableNotification: false,
                    DisableWebPagePreview: true),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            // The body explains a 400 (an unparsable message) and a 403 (the bot was blocked or
            // never started by this chat), which are the two failures an operator can act on.
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Telegram refused an alert for chat {ChatId} with {StatusCode}: {Response}",
                chatId,
                (int)response.StatusCode,
                Redact(Shorten(body)));
        }
        catch (Exception exception)
        {
            // Deliberately catching everything, cancellation included, and deliberately not
            // passing the exception object: its message can carry the request URI, and the
            // request URI carries the bot token.
            logger.LogWarning(
                "An operator alert could not be delivered to Telegram chat {ChatId}: "
                + "{Failure}: {Reason}",
                chatId,
                exception.GetType().Name,
                Redact(Shorten(exception.Message)));
        }
    }

    /// <summary>
    /// Removes the bot token from anything about to be logged.
    /// </summary>
    /// <remarks>
    /// The token is in the URL, and an HTTP stack is entitled to put the URL in an error message.
    /// This is the last line rather than the only one — the client's request logging is removed at
    /// registration and nothing else here writes the URL — but it costs one string scan on a path
    /// that only runs when something has already gone wrong.
    /// </remarks>
    internal string Redact(string value) =>
        string.IsNullOrEmpty(options.BotToken)
            ? value
            : value.Replace(options.BotToken, "[redacted]", StringComparison.Ordinal);

    private static string Shorten(string value)
    {
        const int maximumLength = 500;
        string normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private sealed record SendMessageRequest(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("parse_mode")] string ParseMode,
        [property: JsonPropertyName("disable_notification")] bool DisableNotification,
        [property: JsonPropertyName("disable_web_page_preview")] bool DisableWebPagePreview);
}
