using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AdminMetricsReadStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SnapshotAggregatesRealCountsWithoutError()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        // Seeding a user guarantees the total is at least one and exercises every count query.
        await CreateUserAsync("metrics-user");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AdminMetricsReadStore store = new(context, new FixedTimeProvider(Now));

        AdminMetricsSnapshot snapshot = await store.GetAsync(Token);

        Assert.Equal(Now, snapshot.GeneratedAtUtc);
        Assert.True(snapshot.TotalUsers >= 1);
        Assert.True(snapshot.ActiveLicenses >= 0);
        Assert.True(snapshot.HeldDiffs >= 0);
        Assert.True(snapshot.RevisionsAwaitingReview >= 0);
        Assert.True(snapshot.PollingSourcesOverdue >= 0);
        Assert.True(snapshot.InitialSyncsInProgress >= 0);
        Assert.True(snapshot.CompletedConnections >= 0);
    }

    private async Task<UserSession> CreateUserAsync(string prefix)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        string nonce = Guid.NewGuid().ToString("N");
        return await new UserStore(context).SignInWithGoogleAsync(
            new GoogleIdentity
            {
                Subject = $"{prefix}-{nonce}",
                Email = $"{prefix}-{nonce}@example.com",
                EmailVerified = true,
            },
            UserRole.User,
            Now,
            Token);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
