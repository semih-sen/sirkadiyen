namespace Sirkadiyen.Application.Vault;

/// <summary>
/// How a vault note job prepares its workspace and how far it may reach into existing notes.
/// </summary>
public sealed record VaultNoteOptions
{
    /// <summary>
    /// The directory under which each job gets its own workspace. Everything under it belongs to this
    /// feature: the startup sweep deletes whatever it finds there.
    /// </summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>
    /// The most existing notes one job may rewrite to add a backlink. The agent may ask for more; the
    /// rest are reported as skipped rather than written, because every one of them is an overwrite of
    /// a note the user wrote.
    /// </summary>
    public int MaxBacklinks { get; init; } = 5;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkspaceRoot);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBacklinks);
    }
}
