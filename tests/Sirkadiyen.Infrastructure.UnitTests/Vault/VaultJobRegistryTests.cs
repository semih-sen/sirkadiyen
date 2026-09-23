using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultJobRegistryTests
{
    private static readonly VaultNoteRequest Request = VaultNoteRequest.Create("x", null, null, out _)!;

    [Fact]
    public async Task Submit_queues_the_job()
    {
        VaultJobRegistry registry = Create(maxRetainedJobs: 10);

        VaultJobView submitted = registry.Submit(Request);

        Assert.Equal(VaultJobStatus.Queued, submitted.Status);
        Assert.Same(Request, registry.FindRequest(submitted.Id));
        await using IAsyncEnumerator<Guid> queue = registry.ReadQueueAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await queue.MoveNextAsync());
        Assert.Equal(submitted.Id, queue.Current);
    }

    [Fact]
    public void Update_stamps_completion_once()
    {
        VaultJobRegistry registry = Create(maxRetainedJobs: 10);
        Guid id = registry.Submit(Request).Id;

        VaultJobView? running = registry.Update(id, static view => view with { Status = VaultJobStatus.Generating });
        VaultJobView? finished = registry.Update(id, static view => view with { Status = VaultJobStatus.Succeeded });

        Assert.Null(running?.CompletedAtUtc);
        Assert.Equal(TestClock.Now, finished?.CompletedAtUtc);
    }

    [Fact]
    public void Retention_forgets_only_finished_jobs()
    {
        VaultJobRegistry registry = Create(maxRetainedJobs: 2);
        Guid queued = registry.Submit(Request).Id;
        Guid finished = registry.Submit(Request).Id;
        registry.Update(finished, static view => view with { Status = VaultJobStatus.Failed });

        Guid latest = registry.Submit(Request).Id;

        Assert.NotNull(registry.Find(queued));
        Assert.Null(registry.Find(finished));
        Assert.NotNull(registry.Find(latest));
    }

    private static VaultJobRegistry Create(int maxRetainedJobs) =>
        new(new VaultNoteOptions { WorkspaceRoot = "unused", MaxRetainedJobs = maxRetainedJobs }, new TestClock());

    private sealed class TestClock : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
