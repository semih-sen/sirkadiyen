namespace Sirkadiyen.Application.Vault;

/// <summary>
/// A request for a new vault note. <see cref="Folder"/> and <see cref="Title"/> are optional overrides
/// of what the agent would otherwise propose; both are already normalized once
/// <see cref="Create"/> has accepted them.
/// </summary>
public sealed record VaultNoteRequest
{
    /// <summary>Long enough for a detailed brief; short enough that the prompt leaves room for the catalog.</summary>
    public const int MaxPromptLength = 8000;

    private VaultNoteRequest(string prompt, string? folder, string? title)
    {
        Prompt = prompt;
        Folder = folder;
        Title = title;
    }

    public string Prompt { get; }

    /// <summary>The folder the user chose (empty string for the root), or null to let the agent propose one.</summary>
    public string? Folder { get; }

    /// <summary>The title the user chose, or null to let the agent propose one.</summary>
    public string? Title { get; }

    /// <summary>Validates and normalizes raw input. Returns null and a user-facing reason when refused.</summary>
    public static VaultNoteRequest? Create(string? prompt, string? folder, string? title, out string? error)
    {
        error = null;
        string trimmedPrompt = prompt?.Trim() ?? string.Empty;
        if (trimmedPrompt.Length == 0)
        {
            error = "İstek metni boş olamaz.";
            return null;
        }

        if (trimmedPrompt.Length > MaxPromptLength)
        {
            error = $"İstek metni en fazla {MaxPromptLength} karakter olabilir.";
            return null;
        }

        string? normalizedFolder = null;
        if (folder is not null)
        {
            normalizedFolder = VaultPathPolicy.NormalizeFolder(folder, out error);
            if (normalizedFolder is null)
            {
                return null;
            }
        }

        string? normalizedTitle = null;
        if (!string.IsNullOrWhiteSpace(title))
        {
            normalizedTitle = VaultPathPolicy.NormalizeTitle(title, out error);
            if (normalizedTitle is null)
            {
                return null;
            }
        }

        return new VaultNoteRequest(trimmedPrompt, normalizedFolder, normalizedTitle);
    }
}
