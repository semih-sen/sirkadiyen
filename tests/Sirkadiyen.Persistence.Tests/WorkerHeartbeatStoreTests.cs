using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Observability;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Observability.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class WorkerHeartbeatStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordUpsertsOneRowPerInstanceAndListsNewestFirst()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        WorkerHeartbeatStore store = new(context);
        string instanceA = $"host-a:{Guid.NewGuid():N}";
        string instanceB = $"host-b:{Guid.NewGuid():N}";
        DateTimeOffset cutoff = Now.AddDays(-1);

        await store.RecordAsync(Beat(instanceA, "initial-calendar-sync"), Now, cutoff, Token);
        // A second beat for the same instance updates the row rather than inserting a second.
        await store.RecordAsync(
            Beat(instanceA, "calendar-maintenance"),
            Now.AddSeconds(30),
            cutoff,
            Token);
        await store.RecordAsync(Beat(instanceB, "waiting"), Now.AddSeconds(45), cutoff, Token);

        IReadOnlyList<WorkerInstanceView> all = await store.ListAsync(Token);
        WorkerInstanceView a = Assert.Single(all, view => view.InstanceId == instanceA);
        WorkerInstanceView b = Assert.Single(all, view => view.InstanceId == instanceB);

        Assert.Equal("calendar-maintenance", a.CurrentStage);
        Assert.Equal(Now.AddSeconds(30), a.LastHeartbeatAtUtc);

        // Newest report first: B reported at +45s, A at +30s.
        List<WorkerInstanceView> ordered = [.. all.Where(v => v.InstanceId == instanceA || v.InstanceId == instanceB)];
        Assert.Equal(instanceB, ordered[0].InstanceId);
        Assert.True(ordered.IndexOf(b) < ordered.IndexOf(a));
    }

    [Fact]
    public async Task RecordPrunesInstancesNotSeenSinceTheRetentionCutoff()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        WorkerHeartbeatStore store = new(context);
        string stale = $"stale:{Guid.NewGuid():N}";
        string live = $"live:{Guid.NewGuid():N}";

        // A stale instance last reported two days ago.
        await store.RecordAsync(Beat(stale, "waiting"), Now.AddDays(-2), Now.AddDays(-3), Token);
        // A live instance reports now with a one-day retention cutoff, which prunes the stale row.
        await store.RecordAsync(Beat(live, "waiting"), Now, Now.AddDays(-1), Token);

        context.ChangeTracker.Clear();
        List<string> remaining = await context.WorkerInstanceHeartbeats
            .Where(row => row.InstanceId == stale || row.InstanceId == live)
            .Select(row => row.InstanceId)
            .ToListAsync(Token);

        Assert.Equal([live], remaining);
    }

    private static WorkerHeartbeatBeat Beat(string instanceId, string stage) => new()
    {
        InstanceId = instanceId,
        StartedAtUtc = Now.AddMinutes(-10),
        Status = "healthy",
        CurrentStage = stage,
        LastActivityAtUtc = Now,
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
