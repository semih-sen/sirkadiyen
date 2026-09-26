using System.Text;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Decides which vault paths a job may read and write. Every path the agent proposes passes through
/// here before it reaches the store, so a proposal can never climb out of the vault, land in
/// Obsidian's own configuration, or produce a title Obsidian cannot link to.
/// </summary>
public static class VaultPathPolicy
{
    public const string NoteExtension = ".md";

    /// <summary>Long enough for any real title; short enough that a path stays well inside S3's key limit.</summary>
    public const int MaxSegmentLength = 120;

    /// <summary>The job table's path column, and S3's key limit, are 1024.</summary>
    public const int MaxNotePathLength = 1024;

    /// <summary>
    /// File-system reserved characters plus <c># ^ [ ] |</c>, which Obsidian reads as heading, block,
    /// link, and alias syntax inside <c>[[...]]</c> - a note titled with one of them cannot be linked to.
    /// </summary>
    private static readonly char[] ForbiddenCharacters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|', '#', '^', '[', ']'];

    /// <summary>
    /// Whether a path lies under a dot-segment such as <c>.obsidian/</c> or <c>.trash/</c>. Those hold
    /// the vault's settings and deleted notes; neither is something a new note should link to or land in.
    /// </summary>
    public static bool IsHidden(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Split('/').Any(static segment => segment.StartsWith('.'));
    }

    public static bool IsNote(string path) =>
        path.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase) && !IsHidden(path);

    /// <summary>The folder part of a path, or the empty string for the vault root.</summary>
    public static string FolderOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    /// <summary>The file name without folder or <c>.md</c> extension.</summary>
    public static string TitleOf(string path)
    {
        string name = path[(path.LastIndexOf('/') + 1)..];
        return name.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^NoteExtension.Length]
            : name;
    }

    /// <summary>
    /// Normalizes the path of an existing note the user picked (ADR-170). Existing notes are named by
    /// the user, so the stricter rules for a new note's title do not apply; what is refused is what no
    /// note path can be: a non-note, a hidden or <c>..</c> segment, an empty segment, or a path longer
    /// than an S3 key. Whether the note exists is for the caller to check against the vault.
    /// </summary>
    public static string? NormalizeNotePath(string? path, out string? error)
    {
        error = null;
        string trimmed = (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        if (trimmed.Length == 0)
        {
            error = "Not yolu boş olamaz.";
            return null;
        }

        if (!IsNote(trimmed)
            || trimmed.Length > MaxNotePathLength
            || trimmed.Split('/').Any(static segment => segment.Trim().Length == 0))
        {
            error = $"'{trimmed}' geçerli bir not yolu değil.";
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Normalizes a folder the user asked for. The empty string is the vault root. Returns null and a
    /// reason when any segment is unusable.
    /// </summary>
    public static string? NormalizeFolder(string? folder, out string? error)
    {
        error = null;
        string trimmed = (folder ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        foreach (string segment in trimmed.Split('/'))
        {
            if (!IsValidSegment(segment))
            {
                error = $"'{segment}' geçerli bir klasör adı değil.";
                return null;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Normalizes a title the user asked for, accepting it with or without the <c>.md</c> extension.
    /// Returns null and a reason when it is unusable; a user-supplied title is refused rather than
    /// rewritten, because silently renaming what someone typed is worse than asking them again.
    /// </summary>
    public static string? NormalizeTitle(string? title, out string? error)
    {
        error = null;
        string trimmed = StripExtension((title ?? string.Empty).Trim());
        if (!IsValidSegment(trimmed))
        {
            error = $"'{title}' geçerli bir not adı değil.";
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Makes an agent-proposed title usable by replacing what Obsidian or the file system would reject.
    /// Unlike <see cref="NormalizeTitle"/> this never refuses: by the time the agent proposes a title
    /// the note is already written, and losing it over a stray colon would be the worse outcome.
    /// Returns null only when nothing usable is left.
    /// </summary>
    public static string? SanitizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        StringBuilder builder = new();
        foreach (char character in StripExtension(title.Trim()))
        {
            builder.Append(char.IsControl(character) || ForbiddenCharacters.Contains(character) ? ' ' : character);
        }

        string collapsed = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .TrimStart('.');
        if (collapsed.Length > MaxSegmentLength)
        {
            collapsed = collapsed[..MaxSegmentLength].TrimEnd();
        }

        return IsValidSegment(collapsed) ? collapsed : null;
    }

    /// <summary>
    /// Chooses where a new note is written. The title is made unique across the whole vault, not just
    /// its folder, so the bare title is always an unambiguous link target - the backlinks this job adds
    /// can then be plain <c>[[Title]]</c>, as a hand-written Zettelkasten link would be.
    /// </summary>
    /// <param name="folder">An already normalized folder, or null to use the vault root.</param>
    /// <param name="folderMustExist">
    /// True for a folder the agent proposed: it may only choose among existing folders, and an unknown
    /// one falls back to the root with a warning. A folder the user named is created if it is new.
    /// </param>
    /// <param name="title">An already normalized or sanitized title.</param>
    public static VaultNotePlacement Place(
        VaultCatalog catalog,
        string? folder,
        bool folderMustExist,
        string title)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        string? warning = null;
        string chosenFolder = folder ?? string.Empty;
        if (folderMustExist && chosenFolder.Length > 0 && !catalog.HasFolder(chosenFolder))
        {
            warning = $"Önerilen '{chosenFolder}' klasörü vault'ta yok; not köke yazıldı.";
            chosenFolder = string.Empty;
        }

        string uniqueTitle = title;
        for (int suffix = 2; catalog.IsTitleTaken(uniqueTitle); suffix++)
        {
            uniqueTitle = $"{title}_{suffix}";
        }

        string path = chosenFolder.Length == 0
            ? uniqueTitle + NoteExtension
            : $"{chosenFolder}/{uniqueTitle}{NoteExtension}";
        return new VaultNotePlacement(path, uniqueTitle, warning);
    }

    private static string StripExtension(string value) =>
        value.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase) ? value[..^NoteExtension.Length] : value;

    private static bool IsValidSegment(string segment) =>
        segment.Length is > 0 and <= MaxSegmentLength
        && segment == segment.Trim()
        && !segment.StartsWith('.')
        && !segment.Any(static character => char.IsControl(character) || ForbiddenCharacters.Contains(character));
}

/// <summary>Where a new note goes; <see cref="Title"/> is also its link target.</summary>
public sealed record VaultNotePlacement(string Path, string Title, string? Warning);
