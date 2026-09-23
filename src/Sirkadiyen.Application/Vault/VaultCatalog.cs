namespace Sirkadiyen.Application.Vault;

/// <summary>
/// The notes and folders a job may link to and write into, read once from the store at the start of
/// the job. Title comparisons ignore case because Obsidian resolves links that way and the vault is
/// also opened on case-insensitive file systems, where two titles differing only in case collide.
/// </summary>
public sealed class VaultCatalog
{
    private readonly Dictionary<string, VaultNote> byLinkTarget;
    private readonly Dictionary<string, VaultNote> byPath;
    private readonly HashSet<string> titles;
    private readonly HashSet<string> folders;

    private VaultCatalog(IReadOnlyList<VaultNote> notes, IReadOnlyList<string> folderList)
    {
        Notes = notes;
        Folders = folderList;
        byLinkTarget = IndexBy(notes, static note => note.LinkTarget);
        byPath = IndexBy(notes, static note => note.Path);
        titles = notes.Select(static note => note.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        folders = folderList.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<VaultNote> Notes { get; }

    /// <summary>Every non-hidden folder holding at least one object, excluding the root.</summary>
    public IReadOnlyList<string> Folders { get; }

    public static VaultCatalog Build(IEnumerable<string> objectPaths)
    {
        ArgumentNullException.ThrowIfNull(objectPaths);

        List<string> visible = objectPaths
            .Select(static path => path.Replace('\\', '/').TrimStart('/'))
            .Where(static path => path.Length > 0 && !VaultPathPolicy.IsHidden(path))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<string> notePaths = visible
            .Where(VaultPathPolicy.IsNote)
            .Order(StringComparer.Ordinal)
            .ToList();

        // A title shared by two notes cannot be linked by title alone - Obsidian would resolve it to
        // one of them - so those notes are offered to the agent by their path instead.
        HashSet<string> ambiguousTitles = notePaths
            .GroupBy(VaultPathPolicy.TitleOf, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<VaultNote> notes = notePaths
            .Select(path =>
            {
                string title = VaultPathPolicy.TitleOf(path);
                string linkTarget = ambiguousTitles.Contains(title)
                    ? path[..^VaultPathPolicy.NoteExtension.Length]
                    : title;
                return new VaultNote(path, title, linkTarget);
            })
            .ToList();

        // S3 has no directories; a folder exists exactly when something is stored under it.
        List<string> folderList = visible
            .SelectMany(static path => AncestorsOf(VaultPathPolicy.FolderOf(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new VaultCatalog(notes, folderList);
    }

    /// <summary>
    /// Finds a note by the link target the agent was offered, also accepting its path with or without
    /// the extension, since an agent echoing a target back may well add either.
    /// </summary>
    public VaultNote? Find(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        string trimmed = target.Trim().Trim('[', ']').Trim();
        if (byLinkTarget.TryGetValue(trimmed, out VaultNote? note) || byPath.TryGetValue(trimmed, out note))
        {
            return note;
        }

        return byPath.GetValueOrDefault(trimmed + VaultPathPolicy.NoteExtension);
    }

    /// <summary>
    /// Finds the one note a link was probably meant for when it matches no note exactly: the same
    /// words, but with spaces for underscores, other casing, or Turkish letters written without their
    /// marks - the ways a model misspells a title it was given. Null when nothing or more than one
    /// note matches, since a guess between two notes would be a wrong link half the time.
    /// </summary>
    public VaultNote? FindLoosely(string? target)
    {
        if (Find(target) is { } exact)
        {
            return exact;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        string key = LooseKey(target);
        List<VaultNote> matches = Notes
            .Where(note => LooseKey(note.LinkTarget) == key || LooseKey(note.Path) == key)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public bool IsTitleTaken(string title) => titles.Contains(title);

    private static string LooseKey(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.EndsWith(VaultPathPolicy.NoteExtension, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^VaultPathPolicy.NoteExtension.Length];
        }

        System.Text.StringBuilder key = new(trimmed.Length);
        bool pendingSpace = false;
        foreach (char character in trimmed)
        {
            char folded = character switch
            {
                'ı' or 'I' or 'İ' or 'i' => 'i',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ü' or 'Ü' => 'u',
                'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c',
                _ => char.ToLowerInvariant(character),
            };
            if (folded is '_' or '-' || char.IsWhiteSpace(folded))
            {
                pendingSpace = key.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                key.Append(' ');
                pendingSpace = false;
            }

            key.Append(folded);
        }

        return key.ToString();
    }

    public bool HasFolder(string folder) => folders.Contains(folder);

    /// <summary>
    /// S3 keys are case-sensitive, so two objects may differ only in case; the first wins rather than
    /// the catalog failing to build over a vault the user can still open.
    /// </summary>
    private static Dictionary<string, VaultNote> IndexBy(IEnumerable<VaultNote> notes, Func<VaultNote, string> key)
    {
        Dictionary<string, VaultNote> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (VaultNote note in notes)
        {
            index.TryAdd(key(note), note);
        }

        return index;
    }

    private static IEnumerable<string> AncestorsOf(string folder)
    {
        for (string current = folder; current.Length > 0; current = VaultPathPolicy.FolderOf(current))
        {
            yield return current;
        }
    }
}

/// <param name="Path">The note's path relative to the vault root, including <c>.md</c>.</param>
/// <param name="Title">The file name without folder or extension.</param>
/// <param name="LinkTarget">What goes inside <c>[[...]]</c> to link to this note unambiguously.</param>
public sealed record VaultNote(string Path, string Title, string LinkTarget);
