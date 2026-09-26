namespace Sirkadiyen.Application.Vault;

/// <summary>
/// What a vault job was asked to do (ADR-170): write a new note (<see cref="VaultNoteRequest"/>) or add
/// flashcards to one that exists (<see cref="VaultFlashcardRequest"/>). Both arrive already validated.
/// </summary>
public abstract record VaultJobRequest
{
    private protected VaultJobRequest()
    {
    }

    public abstract VaultJobKind Kind { get; }
}

public enum VaultJobKind
{
    /// <summary>A new note, written by the agent, with backlinks from existing notes.</summary>
    Note,

    /// <summary>Spaced Repetition flashcards added to an existing note.</summary>
    Flashcards,
}
