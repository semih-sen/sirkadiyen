using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OperationalFreezeStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreezeAndUnfreezeUpdateTheControlAndAppendAuditRowsAtomically()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        OperationalFreezeStore store = new(context);
        await EnsureUnfrozenAsync(store);
        int auditCountBefore = await context.OperationalFreezeAudits.CountAsync(Token);

        OperationalFreezeChangeResult frozen = await store.SetAsync(
            isFrozen: true,
            "semih",
            "Source columns changed unexpectedly.",
            "incident-42",
            Now,
            Token);

        Assert.Equal(OperationalFreezeChangeOutcome.Changed, frozen.Outcome);
        Assert.True(frozen.State.IsFrozen);
        Assert.Equal("semih", frozen.State.ChangedBy);
        Assert.Equal("Source columns changed unexpectedly.", frozen.State.Reason);

        OperationalFreezeChangeResult repeated = await store.SetAsync(
            isFrozen: true,
            "another-operator",
            "Duplicate request.",
            "incident-duplicate",
            Now.AddMinutes(1),
            Token);
        Assert.Equal(
            OperationalFreezeChangeOutcome.AlreadyInRequestedState,
            repeated.Outcome);

        OperationalFreezeChangeResult unfrozen = await store.SetAsync(
            isFrozen: false,
            "semih",
            "Source repaired and a validation pass is ready.",
            "incident-42",
            Now.AddMinutes(2),
            Token);

        Assert.Equal(OperationalFreezeChangeOutcome.Changed, unfrozen.Outcome);
        Assert.False(unfrozen.State.IsFrozen);
        Assert.Equal(
            auditCountBefore + 2,
            await context.OperationalFreezeAudits.CountAsync(Token));

        var changes = await context.OperationalFreezeAudits
            .AsNoTracking()
            .Where(audit => audit.CorrelationId == "incident-42")
            .OrderByDescending(audit => audit.ChangedAtUtc)
            .ToListAsync(Token);
        Assert.Equal([false, true], changes.Select(change => change.IsFrozen));
        Assert.All(changes, change => Assert.Equal("incident-42", change.CorrelationId));
    }

    [Fact]
    public async Task FreezeChangesWorkWithTheHostsRetryingExecutionStrategy()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        OperationalFreezeStore store = new(context);
        await EnsureUnfrozenAsync(store);

        OperationalFreezeChangeResult result = await store.SetAsync(
            isFrozen: true,
            "integration-test",
            "Prove the transaction runs inside the retry strategy.",
            $"test-{Guid.NewGuid():N}",
            Now,
            Token);

        Assert.Equal(OperationalFreezeChangeOutcome.Changed, result.Outcome);

        await store.SetAsync(
            isFrozen: false,
            "integration-test",
            "Restore the shared fixture state.",
            $"test-{Guid.NewGuid():N}",
            Now.AddSeconds(1),
            Token);
    }

    private static async Task EnsureUnfrozenAsync(OperationalFreezeStore store)
    {
        OperationalFreezeSnapshot current = await store.GetAsync(Token);
        if (current.IsFrozen)
        {
            await store.SetAsync(
                isFrozen: false,
                "integration-test",
                "Restore test precondition.",
                $"test-setup-{Guid.NewGuid():N}",
                Now.AddSeconds(-1),
                Token);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
