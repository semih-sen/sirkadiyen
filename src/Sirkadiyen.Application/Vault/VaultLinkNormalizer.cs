using System.Text.RegularExpressions;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Makes every wiki link in a new note point at a note that exists. The agent is given the exact link
/// targets, but it still sometimes writes <c>[[Otonom Sinir Sistemi]]</c> for
/// <c>Otonom_Sinir_Sistemi</c> - the end-to-end run produced six such links out of nine - and in
/// Obsidian each of those is a dead link that creates an empty note when clicked.
/// </summary>
/// <remarks>
/// Only the agent's new note is normalized. Links in the user's existing notes are theirs, including
/// deliberate links to notes not written yet, and are never rewritten.
/// </remarks>
public static partial class VaultLinkNormalizer
{
    public static VaultLinkNormalization Normalize(string content, VaultCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(catalog);

        List<string> corrected = [];
        List<string> removed = [];
        string normalized = WikiLink().Replace(content, match =>
        {
            string target = match.Groups["target"].Value;
            string anchor = match.Groups["anchor"].Value;
            string alias = match.Groups["alias"].Value;

            VaultNote? note = catalog.FindLoosely(target);
            if (note is null)
            {
                // Plain text keeps the sentence readable; the dead link is gone.
                removed.Add(target.Trim());
                return alias.Length > 0 ? alias[1..] : target.Trim();
            }

            if (string.Equals(target.Trim(), note.LinkTarget, StringComparison.Ordinal))
            {
                return match.Value;
            }

            corrected.Add(target.Trim());

            // Keep what the reader sees: the agent's wording becomes the alias unless it gave one.
            string shown = alias.Length > 0 ? alias : "|" + target.Trim();
            return $"[[{note.LinkTarget}{anchor}{shown}]]";
        });

        return new VaultLinkNormalization(normalized, corrected, removed);
    }

    /// <summary>
    /// <c>[[target#anchor|alias]]</c>, not preceded by <c>!</c>: an embed names an attachment, which the
    /// catalog of notes does not hold.
    /// </summary>
    [GeneratedRegex(@"(?<!!)\[\[(?<target>[^\[\]|#\r\n]+)(?<anchor>#[^\[\]|\r\n]*)?(?<alias>\|[^\[\]\r\n]*)?\]\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WikiLink();
}

/// <param name="Content">The note with every link resolved or removed.</param>
/// <param name="Corrected">Link targets that were misspelled and rewritten to the note they meant.</param>
/// <param name="Removed">Link targets that matched no note and were turned into plain text.</param>
public sealed record VaultLinkNormalization(string Content, IReadOnlyList<string> Corrected, IReadOnlyList<string> Removed);
