using System.Text.RegularExpressions;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Decides whether the agent's edit of an existing note is safe to write back. The agent was asked
/// only to insert a link; an edit that did not insert it, or that lost a noticeable part of what the
/// user had written, is refused so the original stays in the vault untouched.
/// </summary>
public static class VaultBacklinkEditCheck
{
    /// <summary>
    /// Inserting a link into a sentence changes that line, so some original lines may legitimately
    /// disappear; more than this share of them going missing means the note was rewritten.
    /// </summary>
    private const double MaxChangedLineShare = 0.1;

    /// <summary>Returns null when the edit may be written, otherwise the reason it may not.</summary>
    public static string? Evaluate(string original, string edited, string linkTarget)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkTarget);

        // [[Target]], [[Target|alias]] and [[Target#Heading]] all link to the note.
        Regex link = new(
            @"\[\[\s*" + Regex.Escape(linkTarget) + @"\s*(\]\]|\||#)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!link.IsMatch(edited))
        {
            return $"Düzenlenmiş notta [[{linkTarget}]] bağlantısı yok.";
        }

        if (edited.Length < original.Length * (1 - MaxChangedLineShare))
        {
            return "Düzenlenmiş not orijinalinden belirgin şekilde kısa.";
        }

        List<string> originalLines = ContentLines(original);
        HashSet<string> editedLines = [.. ContentLines(edited)];
        int missing = originalLines.Count(line => !editedLines.Contains(line));
        int allowed = Math.Max(1, (int)(originalLines.Count * MaxChangedLineShare));
        return missing > allowed
            ? $"Orijinal satırlardan {missing} tanesi değiştirilmiş veya silinmiş (en fazla {allowed} kabul edilir)."
            : null;
    }

    private static List<string> ContentLines(string text) =>
        text.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .Where(static line => line.Length > 0)
            .ToList();
}
