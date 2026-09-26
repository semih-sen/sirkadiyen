namespace Sirkadiyen.Application.Vault;

/// <summary>
/// The Obsidian vault as the API sees it. Paths are relative to the vault root and use forward
/// slashes; the agent never receives an implementation of this, only files the API copies for it.
/// </summary>
public interface IVaultStore
{
    /// <summary>Every object path in the vault, notes and otherwise.</summary>
    Task<IReadOnlyList<string>> ListPathsAsync(CancellationToken cancellationToken);

    /// <summary>Every object in the vault with its size and last change, for the panel's note list.</summary>
    Task<IReadOnlyList<VaultObjectInfo>> ListObjectsAsync(CancellationToken cancellationToken);

    /// <summary>A note's text and the version tag it was read at, or null when it does not exist.</summary>
    Task<VaultDocument?> GetAsync(string path, CancellationToken cancellationToken);

    /// <summary>Writes a new note, refusing to replace one that already exists.</summary>
    Task<VaultWriteOutcome> CreateAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a note only if it is still the version <paramref name="expectedETag"/> names, so an
    /// edit made in Obsidian after the job read the note is never overwritten.
    /// </summary>
    Task<VaultWriteOutcome> ReplaceAsync(
        string path,
        string content,
        string expectedETag,
        CancellationToken cancellationToken);
}

public sealed record VaultObjectInfo(string Path, long Size, DateTimeOffset? LastModifiedUtc);

public sealed record VaultDocument(string Path, string Content, string ETag);

public enum VaultWriteOutcome
{
    Written,

    /// <summary>The object already existed (create) or has changed since it was read (replace).</summary>
    Conflict,
}
