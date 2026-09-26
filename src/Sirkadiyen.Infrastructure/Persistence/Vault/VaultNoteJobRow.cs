namespace Sirkadiyen.Infrastructure.Persistence.Vault;

/// <summary>
/// One personal vault note job as stored (ADR-169). A persistence record rather than a domain entity:
/// the vault is not part of the schedule domain, and its model is the application's
/// <c>VaultJobView</c>, which <see cref="VaultJobStore"/> maps to and from this row.
/// </summary>
internal sealed class VaultNoteJobRow
{
    public const int MaximumPromptLength = 8000;

    public const int MaximumPathLength = 1024;

    public const int MaximumRequestedByLength = 320;

    public Guid Id { get; set; }

    /// <summary>What the job does (ADR-170): <c>Note</c> or <c>Flashcards</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string? RequestedBy { get; set; }

    /// <summary>The new note's brief; empty for a flashcard job, which has none.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>The folder the requester named: empty for the vault root, null to let the agent choose.</summary>
    public string? Folder { get; set; }

    public string? Title { get; set; }

    /// <summary>The existing note a flashcard job converts; null for a note job.</summary>
    public string? TargetPath { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? NotePath { get; set; }

    public string? NoteLink { get; set; }

    /// <summary>The backlink outcomes as a JSON array.</summary>
    public string Backlinks { get; set; } = "[]";

    /// <summary>The warnings as a JSON array of strings.</summary>
    public string Warnings { get; set; } = "[]";

    /// <summary>The written note's deck and card counts as a JSON object; null until a note is written.</summary>
    public string? Flashcards { get; set; }

    public string? Error { get; set; }
}
