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
            Warnings = view.Warnings,
            Error = view.Error,
        };
    }
}

public sealed record VaultBacklinkResponse
{
    public required string Target { get; init; }

    public string? Path { get; init; }

    public required VaultBacklinkStatus Status { get; init; }

    public string? Detail { get; init; }
}

/// <summary>One vault note job as the administration panel shows it: the request beside its outcome.</summary>
public sealed record VaultAdminJobResponse
{
    public required Guid Id { get; init; }

    public required VaultJobSource Source { get; init; }

    public string? RequestedBy { get; init; }

    public required string Prompt { get; init; }

    public string? Folder { get; init; }

    public string? Title { get; init; }

    public required VaultJobStatus Status { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? NotePath { get; init; }

    public string? NoteLink { get; init; }

    public required IReadOnlyList<VaultBacklinkResponse> Backlinks { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public string? Error { get; init; }

    public static VaultAdminJobResponse From(VaultJobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        VaultJobResponse job = VaultJobResponse.From(record.View);
        return new VaultAdminJobResponse
        {
            Id = job.Id,
            Source = record.Origin.Source,
            RequestedBy = record.Origin.RequestedBy,
            Prompt = record.Request.Prompt,
            Folder = record.Request.Folder,
            Title = record.Request.Title,
            Status = job.Status,
            CreatedAtUtc = job.CreatedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            NotePath = job.NotePath,
            NoteLink = job.NoteLink,
            Backlinks = job.Backlinks,
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
