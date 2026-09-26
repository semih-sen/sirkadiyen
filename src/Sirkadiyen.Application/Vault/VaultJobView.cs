namespace Sirkadiyen.Application.Vault;

/// <summary>A snapshot of one note job, as the status endpoint reports it.</summary>
public sealed record VaultJobView
{
    public required Guid Id { get; init; }

    public required VaultJobStatus Status { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>The vault path the new note was written to, once it has been.</summary>
    public string? NotePath { get; init; }

    /// <summary>What goes inside <c>[[...]]</c> to link to the new note.</summary>
    public string? NoteLink { get; init; }

    public IReadOnlyList<VaultBacklinkOutcome> Backlinks { get; init; } = [];

    /// <summary>The deck and cards the written note holds (ADR-170); null until a note has been written.</summary>
    public VaultFlashcardSummary? Flashcards { get; init; }

    /// <summary>Things that went differently than asked without failing the job, such as a folder fallback.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Why the job failed; null unless <see cref="Status"/> is <see cref="VaultJobStatus.Failed"/>.</summary>
    public string? Error { get; init; }

    public bool IsFinished => Status is VaultJobStatus.Succeeded or VaultJobStatus.Failed;
}

public enum VaultJobStatus
{
    Queued,
    Cataloging,
    Generating,
    Uploading,
    Backlinking,
    Succeeded,
    Failed,
}

/// <param name="Target">The link target the agent proposed.</param>
/// <param name="Path">The vault path it resolved to, or null when it matched no note.</param>
/// <param name="Detail">Why the note was skipped or failed; null when it was updated.</param>
public sealed record VaultBacklinkOutcome(string Target, string? Path, VaultBacklinkStatus Status, string? Detail);

public enum VaultBacklinkStatus
{
    Updated,

    /// <summary>The note changed or disappeared after the job read it; the newer version was kept.</summary>
    SkippedChanged,

    /// <summary>The target matched no note, or the agent's edit failed the safety check.</summary>
    SkippedInvalid,

    /// <summary>Beyond the per-job backlink limit.</summary>
    SkippedLimit,

    /// <summary>The backlink run itself failed, so the note was left as it was.</summary>
    Failed,
}
