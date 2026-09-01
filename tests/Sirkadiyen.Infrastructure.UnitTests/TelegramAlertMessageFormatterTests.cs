using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Infrastructure.Notifications;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The message an operator actually reads, checked without a bot token or a network (ADR-144).
/// </summary>
public sealed class TelegramAlertMessageFormatterTests
{
    [Fact]
    public void AnAlertBecomesATitleADetailAndOneLinePerField()
    {
        string message = TelegramAlertMessageFormatter.Format(new OperatorAlert
        {
            Title = "Fark tutuldu",
            Severity = OperatorAlertSeverity.Warning,
            DedupeKey = "diff:1",
            Detail = "Serbest bırakılana kadar takvime yazılmaz.",
            Fields =
            [
                new OperatorAlertField("Kaynak", "G1-TR-ANNUAL"),
                new OperatorAlertField("Değişiklik", "3 yeni, 0 silinen"),
            ],
        });

        Assert.StartsWith("⚠️ <b>Fark tutuldu</b>", message, StringComparison.Ordinal);
        Assert.Contains("Serbest bırakılana kadar", message, StringComparison.Ordinal);
        Assert.Contains(
            "<b>Kaynak:</b> <code>G1-TR-ANNUAL</code>",
            message,
            StringComparison.Ordinal);
        Assert.Contains(
            "<b>Değişiklik:</b> <code>3 yeni, 0 silinen</code>",
            message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SeverityIsTheOnlyThingThatChangesTheIcon()
    {
        Assert.StartsWith("ℹ️", Format(OperatorAlertSeverity.Info), StringComparison.Ordinal);
        Assert.StartsWith("⚠️", Format(OperatorAlertSeverity.Warning), StringComparison.Ordinal);
        Assert.StartsWith("🔴", Format(OperatorAlertSeverity.Error), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryValueIsEscapedBecauseTelegramParsesTheMessageAsHtml()
    {
        // An exception message quoting a URL or a generic type is the realistic source of this,
        // and an unescaped angle bracket is a 400 that loses the whole alert rather than a
        // cosmetic problem.
        string message = TelegramAlertMessageFormatter.Format(new OperatorAlert
        {
            Title = "<b>fail</b> & stop",
            Severity = OperatorAlertSeverity.Error,
            DedupeKey = "k",
            Detail = "a < b",
            Fields = [new OperatorAlertField("Ayrıntı", "List<int> & co")],
        });

        Assert.Contains("&lt;b&gt;fail&lt;/b&gt; &amp; stop", message, StringComparison.Ordinal);
        Assert.Contains("a &lt; b", message, StringComparison.Ordinal);
        Assert.Contains("List&lt;int&gt; &amp; co", message, StringComparison.Ordinal);

        // The tags the formatter itself writes are the only unescaped markup left.
        Assert.Contains("<code>", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAmpersandIsEscapedBeforeTheAngleBrackets()
    {
        // Escaping in the other order turns "<" into "&lt;" and then into "&amp;lt;", which
        // Telegram renders literally.
        Assert.Equal("&lt;", TelegramAlertMessageFormatter.Escape("<"));
        Assert.Equal("&amp;lt;", TelegramAlertMessageFormatter.Escape("&lt;"));
    }

    [Fact]
    public void AnOverLongMessageIsCutAtALineBoundarySoNoTagIsLeftOpen()
    {
        OperatorAlert alert = new()
        {
            Title = "Uzun",
            Severity = OperatorAlertSeverity.Info,
            DedupeKey = "k",
            Fields = [.. Enumerable.Range(0, 500).Select(index =>
                new OperatorAlertField($"Alan {index}", new string('x', 60)))],
        };

        string message = TelegramAlertMessageFormatter.Format(alert);

        Assert.True(message.Length <= TelegramAlertMessageFormatter.MaximumLength);
        Assert.EndsWith("…", message, StringComparison.Ordinal);

        // Every tag this formatter writes is opened and closed on one line, so a cut at a line
        // boundary leaves balanced markup — which is what Telegram refuses a message for.
        Assert.Equal(
            Occurrences(message, "<code>"),
            Occurrences(message, "</code>"));
        Assert.Equal(Occurrences(message, "<b>"), Occurrences(message, "</b>"));
    }

    [Fact]
    public void AnAlertWithNoDetailAndNoFieldsIsJustItsTitle()
    {
        string message = TelegramAlertMessageFormatter.Format(new OperatorAlert
        {
            Title = "Boru hattı bekliyor",
            Severity = OperatorAlertSeverity.Warning,
            DedupeKey = "pipeline-stalled",
        });

        Assert.Equal("⚠️ <b>Boru hattı bekliyor</b>", message);
    }

    private static int Occurrences(string value, string token)
    {
        int count = 0;
        int index = value.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = value.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Format(OperatorAlertSeverity severity) =>
        TelegramAlertMessageFormatter.Format(new OperatorAlert
        {
            Title = "Başlık",
            Severity = severity,
            DedupeKey = "k",
        });
}
