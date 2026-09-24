using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultJobRegistryTests
{
    private static readonly VaultNoteRequest Request = VaultNoteRequest.Create("x", null, null, out _)!;

    private readonly InMemoryVaultJobStore store = new();
    private readonly VaultJobQueue queue = new();

    [Fact]
    public async Task Submit_stores_and_queues_the_job()
    {
        VaultJobRegistry registry = Create();
        VaultJobOrigin origin = new(VaultJobSource.Admin, "admin@example.com");

        VaultJobView submitted = await registry.SubmitAsync(Request, origin, CancellationToken.None);

        Assert.Equal(VaultJobStatus.Queued, submitted.Status);
        VaultJobRecord stored = (await registry.FindRecordAsync(submitted.Id, CancellationToken.None))!;
        Assert.Same(Request, stored.Request);
        Assert.Equal(origin, stored.Origin);
        Assert.Equal([submitted.Id], await DrainAsync(1));
    }

    [Fact]
    public async Task Update_stamps_completion_once()
    {
        VaultJobRegistry registry = Create();
        Guid id = (await registry.SubmitAsync(Request, VaultJobOrigin.Shortcut, CancellationToken.None)).Id;

        VaultJobView? running = await registry.UpdateAsync(id, static view => view with { Status = VaultJobStatus.Generating }, CancellationToken.None);
        VaultJobView? finished = await registry.UpdateAsync(id, static view => view with { Status = VaultJobStatus.Succeeded }, CancellationToken.None);

        Assert.Null(running?.CompletedAtUtc);
        Assert.Equal(TestClock.Now, finished?.CompletedAtUtc);
    }

    [Fact]
    public async Task Recovery_requeues_queued_jobs_and_fails_interrupted_ones()
    {
        VaultJobRegistry previous = Create();
        Guid queued = (await previous.SubmitAsync(Request, VaultJobOrigin.Shortcut, CancellationToken.None)).Id;
        Guid running = (await previous.SubmitAsync(Request, VaultJobOrigin.Shortcut, CancellationToken.None)).Id;
        Guid finished = (await previous.SubmitAsync(Request, VaultJobOrigin.Shortcut, CancellationToken.None)).Id;
        await previous.UpdateAsync(running, static view => view with { Status = VaultJobStatus.Generating }, CancellationToken.None);
        await previous.UpdateAsync(finished, static view => view with { Status = VaultJobStatus.Succeeded }, CancellationToken.None);
        await DrainAsync(3);

        // A new process: the queue is empty, the table is not.
        VaultJobQueue restartedQueue = new();
        VaultJobRecovery recovery = await new VaultJobRegistry(store, restartedQueue, new TestClock())
            .RecoverAsync(CancellationToken.None);

        Assert.Equal(new VaultJobRecovery(Requeued: 1, Interrupted: 1), recovery);
        Assert.Equal([queued], await DrainAsync(restartedQueue, 1));
        VaultJobView interrupted = (await store.FindAsync(running, CancellationToken.None))!.View;
        Assert.Equal(VaultJobStatus.Failed, interrupted.Status);
        Assert.Equal(VaultJobRegistry.InterruptedError, interrupted.Error);
        Assert.Equal(VaultJobStatus.Succeeded, (await store.FindAsync(finished, CancellationToken.None))!.View.Status);
    }

    private VaultJobRegistry Create() => new(store, queue, new TestClock());

    private Task<List<Guid>> DrainAsync(int count) => DrainAsync(queue, count);

    private static async Task<List<Guid>> DrainAsync(VaultJobQueue source, int count)
    {
        List<Guid> ids = [];
        await using IAsyncEnumerator<Guid> reader = source.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        while (ids.Count < count && await reader.MoveNextAsync())
        {
            ids.Add(reader.Current);
        }

        return ids;
    }

    private sealed class TestClock : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
