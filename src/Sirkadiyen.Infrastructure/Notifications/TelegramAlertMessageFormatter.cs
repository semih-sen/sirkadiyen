using System.Text;
using Sirkadiyen.Application.Notifications;

namespace Sirkadiyen.Infrastructure.Notifications;

/// <summary>
/// Renders an <see cref="OperatorAlert"/> as one Telegram HTML message (ADR-144).
/// </summary>
/// <remarks>
/// Pure and separate from the transport so the thing an operator actually reads can be tested
/// without a bot token or a network. Telegram's HTML mode is a parser, not a renderer: an
/// unescaped <c>&lt;</c> in a source name or an exception message is a 400 for the whole message,
/// so every value is escaped and only this file writes a tag.
/// </remarks>
public static class TelegramAlertMessageFormatter
{
    /// <summary>Telegram refuses a longer message outright.</summary>
    public const int MaximumLength = 4096;

    private const string Ellipsis = "\n…";

    public static string Format(OperatorAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        StringBuilder builder = new();
        builder.Append(Icon(alert.Severity))
            .Append(" <b>")
            .Append(Escape(alert.Title))
            .Append("</b>");

        if (!string.IsNullOrWhiteSpace(alert.Detail))
        {
            builder.Append("\n\n").Append(Escape(alert.Detail.Trim()));
        }

        if (alert.Fields.Count > 0)
        {
            builder.Append('\n');
            foreach (OperatorAlertField field in alert.Fields)
            {
                builder.Append("\n<b>")
                    .Append(Escape(field.Label))
                    .Append(":</b> <code>")
                    .Append(Escape(field.Value))
                    .Append("</code>");
            }
        }

        return Truncate(builder.ToString());
    }

    /// <summary>
    /// Escapes the three characters Telegram's HTML mode treats as markup.
    /// </summary>
    /// <remarks>
    /// The ampersand is replaced first, or the escapes written for the angle brackets would
    /// themselves be escaped.
    /// </remarks>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Cuts an over-long message at a tag boundary.
    /// </summary>
    /// <remarks>
    /// Fields are written one per line and every tag is opened and closed on the same line, so
    /// cutting at the last complete line before the limit can never split an entity or leave a
    /// tag open — which Telegram would reject, losing the whole alert to make it shorter.
    /// </remarks>
    private static string Truncate(string message)
    {
        if (message.Length <= MaximumLength)
        {
            return message;
        }

        int budget = MaximumLength - Ellipsis.Length;
        int lastLineBreak = message.LastIndexOf('\n', budget);
        return string.Concat(
            message.AsSpan(0, lastLineBreak > 0 ? lastLineBreak : budget),
            Ellipsis);
    }

    private static string Icon(OperatorAlertSeverity severity) => severity switch
    {
        OperatorAlertSeverity.Error => "🔴",
        OperatorAlertSeverity.Warning => "⚠️",
        _ => "ℹ️",
    };
}
