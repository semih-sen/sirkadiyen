using System.Text;
using System.Text.RegularExpressions;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// The Obsidian Spaced Repetition plugin's card format, as far as the vault's jobs need it (ADR-170):
/// the deck tag a note's cards are filed under, and the cards the plugin will find. The plugin runs
/// with its defaults - <c>#flashcards</c> decks, <c>==highlight==</c> clozes, <c>::</c> single-line
/// and <c>?</c> multi-line cards, a blank line ending a card - so those are what is read here.
/// </summary>
/// <remarks>
/// The scan mirrors the plugin's parser closely enough to count and to check placement, not to review:
/// fenced code blocks and inline code are skipped as the plugin skips them, and a deck tag in the
/// frontmatter applies to the whole note as it does there.
/// </remarks>
public static partial class VaultFlashcards
{
    /// <summary>The plugin's default flashcard tag; every deck is this tag or a nested tag under it.</summary>
    public const string DeckTag = "#flashcards";

    /// <summary>The heading of the section that holds a note's question-and-answer cards.</summary>
    public const string SectionHeading = "## Flashcards";

    /// <summary>
    /// A conversion only adds highlights, a deck tag and a card section; some lines may still be touched
    /// by a highlight the stripping cannot undo, but more than this share going missing means rewriting.
    /// </summary>
    private const double MaxChangedLineShare = 0.1;

    /// <summary>
    /// The deck a note in <paramref name="folder"/> is filed under: <c>#flashcards</c> followed by the
    /// folder path, each segment lower-cased and with Turkish letters folded, as the vault's other tags
    /// are written. Null for the vault root, where the deck is chosen by topic instead.
    /// </summary>
    public static string? DeckForFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        List<string> segments = folder.Split('/')
            .Select(Slug)
            .Where(static segment => segment.Length > 0 && !segment.All(char.IsAsciiDigit))
            .ToList();
        return segments.Count == 0 ? null : DeckTag + "/" + string.Join('/', segments);
    }

    /// <summary>The deck and the number of cards the plugin will find in a note.</summary>
    public static VaultFlashcardSummary Summarize(string content) => Scan(content).Summary;

    /// <summary>
    /// Why the plugin would not file a note's cards as intended, or an empty list when it would: no deck
    /// tag, no cards, a deck tag placed after the first card (cards before it belong to no deck), or a
    /// deck tag sharing its line with text (the plugin then applies it to that one card only).
    /// </summary>
    public static IReadOnlyList<string> Problems(string content)
    {
        ScanResult scan = Scan(content);
        List<string> problems = [];
        if (scan.Summary.Deck is null)
        {
            problems.Add($"Notta {DeckTag} deste etiketi yok; eklenti kartları görmez.");
        }
        else if (scan.DeckLine >= 0 && scan.FirstCardLine >= 0 && scan.DeckLine > scan.FirstCardLine)
        {
            problems.Add("Deste etiketi ilk karttan sonra geliyor; ondan önceki kartlar hiçbir desteye girmez.");
        }
        else if (scan.DeckLine >= 0 && !scan.DeckLineHasOnlyTags)
        {
            problems.Add("Deste etiketi metinle aynı satırda; eklenti onu yalnızca o karta uygular.");
        }

        if (scan.Summary.CardCount == 0)
        {
            problems.Add("Notta flashcard yok: ne ==cloze== ne de soru::cevap kartı bulundu.");
        }

        return problems;
    }

    /// <summary>
    /// Puts a blank line after the deck tag's line when text follows it directly. Without one the plugin
    /// reads the tag as the first line of the card below and files only that card under the deck.
    /// </summary>
    public static string SeparateDeckTag(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        ScanResult scan = Scan(content);
        if (scan.DeckLine < 0 || !scan.DeckLineHasOnlyTags)
        {
            return content;
        }

        string[] lines = content.ReplaceLineEndings("\n").Split('\n');
        if (scan.DeckLine + 1 >= lines.Length || lines[scan.DeckLine + 1].Trim().Length == 0)
        {
            return content;
        }

        List<string> separated = [.. lines];
        separated.Insert(scan.DeckLine + 1, string.Empty);
        return string.Join('\n', separated);
    }

    /// <summary>
    /// Decides whether the agent's flashcard conversion of an existing note may be written back. The
    /// agent was asked to add a deck tag, highlight existing sentences and append a card section; an
    /// edit that filed no cards, touched the frontmatter, or lost a noticeable part of the note's text
    /// is refused so the original stays in the vault untouched. Returns null when the edit may be written.
    /// </summary>
    public static string? EvaluateConversion(string original, string edited)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edited);

        if (Problems(edited) is [string problem, ..])
        {
            return problem;
        }

        if (!string.Equals(Frontmatter(original), Frontmatter(edited), StringComparison.Ordinal))
        {
            return "Notun frontmatter'ı değiştirilmiş.";
        }

        // A highlight is the one change allowed inside the user's text, so both sides are compared
        // with the markers removed; a highlight the user had already made compares equal as well.
        string plainOriginal = RemoveHighlightMarkers(original);
        string plainEdited = RemoveHighlightMarkers(edited);
        if (plainEdited.Length < plainOriginal.Length * (1 - MaxChangedLineShare))
        {
            return "Düzenlenmiş not orijinalinden belirgin şekilde kısa.";
        }

        List<string> originalLines = ContentLines(plainOriginal);
        HashSet<string> editedLines = [.. ContentLines(plainEdited)];
        int missing = originalLines.Count(line => !editedLines.Contains(line));
        int allowed = Math.Max(1, (int)(originalLines.Count * MaxChangedLineShare));
        return missing > allowed
            ? $"Orijinal satırlardan {missing} tanesi değiştirilmiş veya silinmiş (en fazla {allowed} kabul edilir)."
            : null;
    }

    private static ScanResult Scan(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string[] lines = content.ReplaceLineEndings("\n").Split('\n');
        string? deck = null;
        int deckLine = -1;
        bool deckLineHasOnlyTags = false;
        int firstCardLine = -1;
        int clozes = 0;
        int questions = 0;

        int bodyStart = FrontmatterEnd(lines) + 1;
        for (int index = 1; index < bodyStart - 1; index++)
        {
            if (deck is null && FrontmatterDeck().Match(lines[index]) is { Success: true } match)
            {
                deck = "#" + match.Value;
            }
        }

        string? fence = null;
        for (int index = bodyStart; index < lines.Length; index++)
        {
            string trimmed = lines[index].Trim();
            if (fence is not null)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal))
                {
                    fence = null;
                }

                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fence = trimmed[..3];
                continue;
            }

            string text = InlineCode().Replace(lines[index], string.Empty);
            if (deck is null && BodyDeck().Match(text) is { Success: true } tag)
            {
                deck = tag.Value;
                deckLine = index;
                deckLineHasOnlyTags = AnyTag().Replace(text, string.Empty).Trim().Length == 0;
            }

            int lineClozes = Cloze().Count(text);
            int lineQuestions = text.Contains("::", StringComparison.Ordinal) || trimmed is "?" or "??" ? 1 : 0;
            clozes += lineClozes;
            questions += lineQuestions;
            if (firstCardLine < 0 && lineClozes + lineQuestions > 0)
            {
                firstCardLine = index;
            }
        }

        return new ScanResult(new VaultFlashcardSummary(deck, clozes, questions), deckLine, deckLineHasOnlyTags, firstCardLine);
    }

    /// <summary>The line index closing the frontmatter, or -1 when the note has none.</summary>
    private static int FrontmatterEnd(string[] lines)
    {
        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
        {
            return -1;
        }

        for (int index = 1; index < lines.Length; index++)
        {
            if (lines[index].TrimEnd() is "---" or "...")
            {
                return index;
            }
        }

        return -1;
    }

    private static string Frontmatter(string content)
    {
        string[] lines = content.ReplaceLineEndings("\n").Split('\n');
        int end = FrontmatterEnd(lines);
        return end < 0 ? string.Empty : string.Join('\n', lines[..(end + 1)]);
    }

    private static string RemoveHighlightMarkers(string content) =>
        content.Replace("==", string.Empty, StringComparison.Ordinal);

    private static List<string> ContentLines(string text) =>
        text.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToList();

    private static string Slug(string segment)
    {
        StringBuilder slug = new(segment.Length);
        bool pendingDash = false;
        foreach (char character in segment.Trim())
        {
            char folded = character switch
            {
                'ı' or 'I' or 'İ' or 'î' or 'Î' => 'i',
                'â' or 'Â' => 'a',
                'û' or 'Û' => 'u',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ü' or 'Ü' => 'u',
                'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c',
                _ => char.ToLowerInvariant(character),
            };
            if (!char.IsAsciiLetterOrDigit(folded))
            {
                pendingDash = slug.Length > 0;
                continue;
            }

            if (pendingDash)
            {
                slug.Append('-');
                pendingDash = false;
            }

            slug.Append(folded);
        }

        return slug.ToString();
    }

    /// <summary>A deck tag in the note's text: <c>#flashcards</c> or a tag nested under it.</summary>
    [GeneratedRegex(@"(?<![^\s])#flashcards(?:/[\p{L}\p{N}_\-]+)*(?![\p{L}\p{N}_\-/])", RegexOptions.CultureInvariant)]
    private static partial Regex BodyDeck();

    /// <summary>A deck tag in the frontmatter's tag list, written there without the <c>#</c>.</summary>
    [GeneratedRegex(@"(?<=^|[\s\[,'""#-])flashcards(?:/[\p{L}\p{N}_\-]+)*(?=$|[\s\],'""])", RegexOptions.CultureInvariant)]
    private static partial Regex FrontmatterDeck();

    [GeneratedRegex(@"(?<![^\s])#[\p{L}\p{N}_\-/]+", RegexOptions.CultureInvariant)]
    private static partial Regex AnyTag();

    /// <summary>A highlight, which the plugin turns into a cloze deletion.</summary>
    [GeneratedRegex(@"==(?!\s)[^=\n]+?(?<!\s)==", RegexOptions.CultureInvariant)]
    private static partial Regex Cloze();

    [GeneratedRegex(@"`[^`\n]*`", RegexOptions.CultureInvariant)]
    private static partial Regex InlineCode();

    private sealed record ScanResult(VaultFlashcardSummary Summary, int DeckLine, bool DeckLineHasOnlyTags, int FirstCardLine);
}

/// <param name="Deck">The first deck tag, with its <c>#</c>; null when the note has none.</param>
/// <param name="ClozeCount">Highlights, each a cloze deletion and so one card.</param>
/// <param name="QuestionCount">Single-line (<c>::</c>) and multi-line (<c>?</c>) question-and-answer cards.</param>
public sealed record VaultFlashcardSummary(string? Deck, int ClozeCount, int QuestionCount)
{
    public int CardCount => ClozeCount + QuestionCount;
}
