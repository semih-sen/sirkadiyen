using System.Threading.Channels;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// The in-process hand-off from a submitted job to the processor. It carries only ids: the job itself
/// lives in <see cref="IVaultJobStore"/>, so an id lost with the process is re-queued from the table
/// at the next startup. The queue has a single reader, so jobs run one at a time.
/// </summary>
public sealed class VaultJobQueue
{
    private readonly Channel<Guid> queue = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    // Unbounded, so the write cannot fail while the channel is open, and nothing ever completes it.
    public void Enqueue(Guid jobId) => queue.Writer.TryWrite(jobId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        queue.Reader.ReadAllAsync(cancellationToken);
}
