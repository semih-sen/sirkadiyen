using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

/// <summary>An <see cref="IVaultJobStore"/> over a dictionary, for tests that need the job's life, not PostgreSQL.</summary>
internal sealed class InMemoryVaultJobStore : IVaultJobStore
{
    private readonly Dictionary<Guid, VaultJobRecord> jobs = [];

    public Task AddAsync(VaultJobRecord job, CancellationToken cancellationToken)
    {
        jobs.Add(job.View.Id, job);
        return Task.CompletedTask;
    }

    public Task<VaultJobRecord?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(jobs.GetValueOrDefault(id));

    public Task<VaultJobView?> UpdateAsync(Guid id, Func<VaultJobView, VaultJobView> change, CancellationToken cancellationToken)
    {
        if (!jobs.TryGetValue(id, out VaultJobRecord? job))
        {
            return Task.FromResult<VaultJobView?>(null);
        }

        VaultJobView updated = change(job.View);
        jobs[id] = job with { View = updated };
        return Task.FromResult<VaultJobView?>(updated);
    }

    public Task<IReadOnlyList<VaultJobRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VaultJobRecord>>(
            [.. jobs.Values.OrderByDescending(static job => job.View.CreatedAtUtc).Take(limit)]);

    public Task<IReadOnlyList<VaultJobRecord>> ListUnfinishedAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VaultJobRecord>>(
            [.. jobs.Values.Where(static job => !job.View.IsFinished).OrderBy(static job => job.View.CreatedAtUtc)]);

    public Task<IReadOnlyList<VaultJobRecord>> ListForNotesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VaultJobRecord>>(
            [.. jobs.Values.Where(static job => job.NotePath is not null).OrderByDescending(static job => job.View.CreatedAtUtc)]);
}
