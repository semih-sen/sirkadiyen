using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Api.Vault;

/// <summary>A request for a new note in the personal vault (ADR-168).</summary>
public sealed record CreateVaultNoteRequest
{
    /// <summary>What the note should cover, in the user's own words.</summary>
    public required string Prompt { get; init; }

    /// <summary>The folder to write into; omitted to let the agent choose among existing folders.</summary>
    public string? Folder { get; init; }

    /// <summary>The note's title; omitted to let the agent propose one.</summary>
    public string? Title { get; init; }
}

/// <summary>The state of one vault note job.</summary>
public sealed record VaultJobResponse
{
    public required Guid Id { get; init; }

    public required VaultJobStatus Status { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? NotePath { get; init; }

    public string? NoteLink { get; init; }

    public required IReadOnlyList<VaultBacklinkResponse> Backlinks { get; init; }

    /// <summary>The written note's deck and card counts (ADR-170); null until a note has been written.</summary>
    public VaultFlashcardSummaryResponse? Flashcards { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public string? Error { get; init; }

    public static VaultJobResponse From(VaultJobView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new VaultJobResponse
        {
            Id = view.Id,
            Status = view.Status,
            CreatedAtUtc = view.CreatedAtUtc,
            CompletedAtUtc = view.CompletedAtUtc,
            NotePath = view.NotePath,
            NoteLink = view.NoteLink,
            Backlinks = view.Backlinks
                .Select(static outcome => new VaultBacklinkResponse
                {
                    Target = outcome.Target,
                    Path = outcome.Path,
                    Status = outcome.Status,
                    Detail = outcome.Detail,
                })
                .ToList(),
            Flashcards = VaultFlashcardSummaryResponse.From(view.Flashcards),
            Warnings = view.Warnings,
            Error = view.Error,
        };
    }
}

/// <summary>The deck a note's cards are filed under and how many cards the plugin will find (ADR-170).</summary>
public sealed record VaultFlashcardSummaryResponse
{
    /// <summary>The first deck tag, e.g. <c>#flashcards/farmakoloji</c>; null when the note has none.</summary>
    public string? Deck { get; init; }

    public required int ClozeCount { get; init; }

    public required int QuestionCount { get; init; }

    public static VaultFlashcardSummaryResponse? From(VaultFlashcardSummary? summary) =>
        summary is null
            ? null
            : new VaultFlashcardSummaryResponse
            {
                Deck = summary.Deck,
                ClozeCount = summary.ClozeCount,
                QuestionCount = summary.QuestionCount,
            };
}

public sealed record VaultBacklinkResponse
{
    public required string Target { get; init; }

    public string? Path { get; init; }

    public required VaultBacklinkStatus Status { get; init; }

    public string? Detail { get; init; }
}

/// <summary>One vault job as the administration panel shows it: the request beside its outcome.</summary>
public sealed record VaultAdminJobResponse
{
    public required Guid Id { get; init; }

    /// <summary>A new note, or flashcards added to an existing one (ADR-170).</summary>
    public required VaultJobKind Kind { get; init; }

    public required VaultJobSource Source { get; init; }

    public string? RequestedBy { get; init; }

    /// <summary>The new note's brief; null for a flashcard job.</summary>
    public string? Prompt { get; init; }

    /// <summary>The existing note a flashcard job converts; null for a note job.</summary>
    public string? TargetPath { get; init; }

    public string? Folder { get; init; }

    public string? Title { get; init; }

    public required VaultJobStatus Status { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? NotePath { get; init; }

    public string? NoteLink { get; init; }

    public required IReadOnlyList<VaultBacklinkResponse> Backlinks { get; init; }

    public VaultFlashcardSummaryResponse? Flashcards { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public string? Error { get; init; }

    public static VaultAdminJobResponse From(VaultJobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        VaultJobResponse job = VaultJobResponse.From(record.View);
        VaultNoteRequest? note = record.Request as VaultNoteRequest;
        return new VaultAdminJobResponse
        {
            Id = job.Id,
            Kind = record.Request.Kind,
            Source = record.Origin.Source,
            RequestedBy = record.Origin.RequestedBy,
            Prompt = note?.Prompt,
            TargetPath = (record.Request as VaultFlashcardRequest)?.NotePath,
            Folder = note?.Folder,
            Title = note?.Title,
            Status = job.Status,
            CreatedAtUtc = job.CreatedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            NotePath = job.NotePath,
            NoteLink = job.NoteLink,
            Backlinks = job.Backlinks,
            Flashcards = job.Flashcards,
            Warnings = job.Warnings,
            Error = job.Error,
        };
    }
}

/// <summary>The panel's job list, and whether this deployment can run a new job at all.</summary>
public sealed record VaultAdminJobListResponse
{
    /// <summary>False when <c>SIRKADIYEN_VAULT__API_KEY</c> is unset: the history is shown, submitting is not.</summary>
    public required bool Enabled { get; init; }

    public required int MaxPromptLength { get; init; }

    public required IReadOnlyList<VaultAdminJobResponse> Jobs { get; init; }
}

/// <summary>A request to add flashcards to an existing note (ADR-170).</summary>
public sealed record CreateVaultFlashcardsRequest
{
    /// <summary>The note's path relative to the vault root, as the note list reports it.</summary>
    public required string Path { get; init; }
}

/// <summary>The vault's notes for the panel (ADR-170), and whether this deployment has a vault at all.</summary>
public sealed record VaultAdminNoteListResponse
{
    /// <summary>False when <c>SIRKADIYEN_VAULT__API_KEY</c> is unset; the list is then empty.</summary>
    public required bool Enabled { get; init; }

    public required IReadOnlyList<VaultAdminNoteResponse> Notes { get; init; }
}

/// <summary>One note in the vault, with what the job history knows about it.</summary>
public sealed record VaultAdminNoteResponse
{
    public required string Path { get; init; }

    public required string Title { get; init; }

    /// <summary>The folder, or the empty string for the vault root.</summary>
    public required string Folder { get; init; }

    public required long SizeBytes { get; init; }

    public DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>The newest job that wrote or converted this note; null when none has.</summary>
    public VaultAdminNoteJobResponse? LatestJob { get; init; }

    /// <summary>
    /// The cards the newest successful job left in the note; null when no job has recorded any. It is
    /// what the job wrote, not a scan of the note now - the content view scans the note itself.
    /// </summary>
    public VaultFlashcardSummaryResponse? Flashcards { get; init; }
}

public sealed record VaultAdminNoteJobResponse
{
    public required Guid Id { get; init; }

    public required VaultJobKind Kind { get; init; }

    public required VaultJobStatus Status { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public string? Error { get; init; }
}

/// <summary>One note's current text, as read from the vault, and the cards the plugin will find in it.</summary>
public sealed record VaultAdminNoteContentResponse
{
    public required string Path { get; init; }

    public required string Title { get; init; }

    public required string Content { get; init; }

    public required VaultFlashcardSummaryResponse Flashcards { get; init; }

    /// <summary>Why the plugin would misfile the note's cards; empty when it would not (or the note has none).</summary>
    public required IReadOnlyList<string> FlashcardProblems { get; init; }
}
