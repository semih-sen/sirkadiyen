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
