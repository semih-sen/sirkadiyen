namespace Sirkadiyen.Application.Vault;

/// <summary>
/// The durable record of every note job (ADR-169): what was asked, by whom, and how it ended. The
/// database row is the job's only state, so a restart loses neither a queued job nor the history
/// the administration panel shows.
/// </summary>
public interface IVaultJobStore
{
    Task AddAsync(VaultJobRecord job, CancellationToken cancellationToken);

    Task<VaultJobRecord?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Applies a change to a job's view and saves it; returns null when the job does not exist.</summary>
    Task<VaultJobView?> UpdateAsync(Guid id, Func<VaultJobView, VaultJobView> change, CancellationToken cancellationToken);

    /// <summary>The most recent jobs, newest first.</summary>
    Task<IReadOnlyList<VaultJobRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Every job that has not finished, oldest first.</summary>
    Task<IReadOnlyList<VaultJobRecord>> ListUnfinishedAsync(CancellationToken cancellationToken);
}

/// <summary>A job as stored: its progress, the request it runs, and where that request came from.</summary>
public sealed record VaultJobRecord(VaultJobView View, VaultNoteRequest Request, VaultJobOrigin Origin);

/// <param name="RequestedBy">The administrator's e-mail when submitted from the panel; null for the shortcut.</param>
public sealed record VaultJobOrigin(VaultJobSource Source, string? RequestedBy)
{
    public static readonly VaultJobOrigin Shortcut = new(VaultJobSource.Shortcut, null);
}

public enum VaultJobSource
{
    /// <summary>The <c>X-Vault-Key</c> endpoint, called by the iPad shortcut.</summary>
    Shortcut,

    /// <summary>The administration panel, under a SuperAdmin session.</summary>
    Admin,
}
