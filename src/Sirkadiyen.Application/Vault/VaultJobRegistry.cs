namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Submits, finds and advances note jobs. Every change is written to <see cref="IVaultJobStore"/>
/// before it is reported, so the table is the status board and the history (ADR-169); the queue only
/// wakes the processor.
/// </summary>
public sealed class VaultJobRegistry(IVaultJobStore store, VaultJobQueue queue, TimeProvider timeProvider)
{
    /// <summary>Why a job that was running when the process stopped is reported as failed.</summary>
    public const string InterruptedError =
        "İş, sunucu yeniden başlarken yarıda kaldı; yeniden gönderin. Not yazılmışsa vault'ta duruyor olabilir.";

    public async Task<VaultJobView> SubmitAsync(
        VaultJobRequest request,
        VaultJobOrigin origin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(origin);

        VaultJobView view = new()
        {
            Id = Guid.NewGuid(),
            Status = VaultJobStatus.Queued,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        // Stored before it is queued: a process that dies in between leaves a queued row, which the
        // next startup picks up, rather than a queued id that points at nothing.
        await store.AddAsync(new VaultJobRecord(view, request, origin), cancellationToken);
        queue.Enqueue(view.Id);
        return view;
    }

    public async Task<VaultJobView?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await store.FindAsync(id, cancellationToken))?.View;

    public Task<VaultJobRecord?> FindRecordAsync(Guid id, CancellationToken cancellationToken) =>
        store.FindAsync(id, cancellationToken);

    public Task<IReadOnlyList<VaultJobRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return store.ListRecentAsync(limit, cancellationToken);
    }

    /// <summary>Applies a change to a job's view. Stamps the completion time when the change finishes it.</summary>
    public Task<VaultJobView?> UpdateAsync(
        Guid id,
        Func<VaultJobView, VaultJobView> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        return store.UpdateAsync(
            id,
            view =>
            {
                VaultJobView updated = change(view);
                return updated.IsFinished && updated.CompletedAtUtc is null
                    ? updated with { CompletedAtUtc = timeProvider.GetUtcNow() }
                    : updated;
            },
            cancellationToken);
    }

    /// <summary>
    /// Picks up what the previous process left behind. Called once before the first job runs: a job
    /// still queued is queued again, in submission order; a job caught mid-run is reported as failed,
    /// because running it again could write its note a second time.
    /// </summary>
    public async Task<VaultJobRecovery> RecoverAsync(CancellationToken cancellationToken)
    {
        int requeued = 0;
        int interrupted = 0;
        foreach (VaultJobRecord job in await store.ListUnfinishedAsync(cancellationToken))
        {
            if (job.View.Status == VaultJobStatus.Queued)
            {
                queue.Enqueue(job.View.Id);
                requeued++;
            }
            else
            {
                await UpdateAsync(
                    job.View.Id,
                    static view => view with { Status = VaultJobStatus.Failed, Error = InterruptedError },
                    cancellationToken);
                interrupted++;
            }
        }

        return new VaultJobRecovery(requeued, interrupted);
    }
}

public sealed record VaultJobRecovery(int Requeued, int Interrupted);
