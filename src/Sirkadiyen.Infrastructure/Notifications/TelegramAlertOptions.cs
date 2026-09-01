using Sirkadiyen.Application.Notifications;

namespace Sirkadiyen.Infrastructure.Notifications;

/// <summary>
/// How the Telegram alert channel is configured (ADR-144).
/// </summary>
/// <remarks>
/// <see cref="BotToken"/> is a credential with full control of the bot. It is read from the
/// environment like every other secret, is never logged, and is deliberately absent from
/// <see cref="Describe"/>, which exists so startup can say what was configured without saying it.
/// </remarks>
public sealed record TelegramAlertOptions
{
    /// <summary>The interval a repeated alert with the same dedupe key is suppressed for.</summary>
    public static readonly TimeSpan DefaultRepeatCooldown = TimeSpan.FromHours(6);

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The BotFather token. Null or empty disables the channel.</summary>
    public string? BotToken { get; init; }

    /// <summary>The chats every alert is sent to. Empty disables the channel.</summary>
    public IReadOnlyList<long> ChatIds { get; init; } = [];

    /// <summary>Alerts below this are not sent at all.</summary>
    public OperatorAlertSeverity MinimumSeverity { get; init; } = OperatorAlertSeverity.Info;

    /// <summary>
    /// How long the same <see cref="OperatorAlert.DedupeKey"/> stays suppressed after it is sent.
    /// </summary>
    /// <remarks>
    /// The stall watch repeats itself every cycle on purpose, so the journal's last line is always
    /// the current state. A chat is not a journal: the same message every fifteen minutes is how a
    /// channel stops being read, which would cost more than the repetition buys.
    /// </remarks>
    public TimeSpan RepeatCooldown { get; init; } = DefaultRepeatCooldown;

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Whether there is anything to send to.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BotToken) && ChatIds.Count > 0;

    /// <summary>A description safe to log: recipient count and thresholds, never the token.</summary>
    public string Describe() =>
        $"{ChatIds.Count} chat(s), minimum severity {MinimumSeverity}, "
        + $"repeat cooldown {RepeatCooldown}";

    public void Validate()
    {
        if (RepeatCooldown < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The Telegram alert repeat cooldown cannot be negative.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The Telegram alert request timeout must be positive.");
        }

        if (ChatIds.Distinct().Count() != ChatIds.Count)
        {
            throw new InvalidOperationException(
                "The same Telegram chat id is configured more than once, which would deliver "
                + "every alert to it twice.");
        }
    }

    /// <summary>
    /// Reads a configured chat id list: comma, semicolon, space or newline separated.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry is not a chat id.</exception>
    public static IReadOnlyList<long> ParseChatIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        List<long> ids = [];
        foreach (string entry in value.Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A chat id is negative for a group and positive for a person, so the sign is data.
            if (!long.TryParse(entry, System.Globalization.CultureInfo.InvariantCulture, out long id))
            {
                throw new InvalidOperationException(
                    $"'{entry}' is not a Telegram chat id. Configure "
                    + "SIRKADIYEN_TELEGRAM__CHAT_IDS as a comma-separated list of numeric ids.");
            }

            ids.Add(id);
        }

        return ids;
    }
}
