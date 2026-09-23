using System.Threading.Channels;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// The in-memory queue and status board for note jobs. Deliberately not persisted: this serves one
/// person's occasional requests, and a job lost to a restart is resubmitted rather than recovered.
/// The queue has a single reader, so jobs run one at a time.
/// </summary>
public sealed class VaultJobRegistry(VaultNoteOptions options, TimeProvider timeProvider)
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private readonly Channel<Guid> queue = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public VaultJobView Submit(VaultNoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        VaultJobView view = new()
        {
            Id = Guid.NewGuid(),
            Status = VaultJobStatus.Queued,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        lock (gate)
        {
            entries[view.Id] = new Entry(request, view);
            ForgetOldestFinished();
        }

        // Unbounded, so the write cannot fail while the channel is open, and nothing ever completes it.
        queue.Writer.TryWrite(view.Id);
        return view;
    }

    public VaultJobView? Find(Guid id)
    {
        lock (gate)
        {
            return entries.TryGetValue(id, out Entry? entry) ? entry.View : null;
        }
    }

    public VaultNoteRequest? FindRequest(Guid id)
    {
        lock (gate)
        {
            return entries.TryGetValue(id, out Entry? entry) ? entry.Request : null;
        }
    }

    /// <summary>Applies a change to a job's view. Stamps the completion time when the change finishes it.</summary>
    public VaultJobView? Update(Guid id, Func<VaultJobView, VaultJobView> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (gate)
        {
            if (!entries.TryGetValue(id, out Entry? entry))
            {
                return null;
            }

            VaultJobView updated = change(entry.View);
            if (updated.IsFinished && updated.CompletedAtUtc is null)
            {
                updated = updated with { CompletedAtUtc = timeProvider.GetUtcNow() };
            }

            entries[id] = entry with { View = updated };
            return updated;
        }
    }

    public IAsyncEnumerable<Guid> ReadQueueAsync(CancellationToken cancellationToken) =>
        queue.Reader.ReadAllAsync(cancellationToken);

    private void ForgetOldestFinished()
    {
        int excess = entries.Count - options.MaxRetainedJobs;
        if (excess <= 0)
        {
            return;
        }

        // Only finished jobs are forgotten; a queued job must stay findable until it has run.
        foreach (Guid id in entries.Values
            .Where(static entry => entry.View.IsFinished)
            .OrderBy(static entry => entry.View.CreatedAtUtc)
            .Take(excess)
            .Select(static entry => entry.View.Id)
            .ToList())
        {
            entries.Remove(id);
        }
    }

    private sealed record Entry(VaultNoteRequest Request, VaultJobView View);
}
