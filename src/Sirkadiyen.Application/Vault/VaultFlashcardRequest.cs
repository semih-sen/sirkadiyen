namespace Sirkadiyen.Application.Vault;

/// <summary>
/// A request to add Spaced Repetition flashcards to an existing note (ADR-170). The path is checked
/// against <see cref="VaultPathPolicy.NormalizeNotePath"/> here; whether the note exists is only known
/// when the job runs.
/// </summary>
public sealed record VaultFlashcardRequest : VaultJobRequest
{
    private VaultFlashcardRequest(string notePath)
    {
        NotePath = notePath;
    }

    public override VaultJobKind Kind => VaultJobKind.Flashcards;

    /// <summary>The note's path relative to the vault root, including <c>.md</c>.</summary>
    public string NotePath { get; }

    /// <summary>Validates and normalizes a note path. Returns null and a user-facing reason when refused.</summary>
    public static VaultFlashcardRequest? Create(string? notePath, out string? error) =>
        VaultPathPolicy.NormalizeNotePath(notePath, out error) is { } path ? new VaultFlashcardRequest(path) : null;
}
