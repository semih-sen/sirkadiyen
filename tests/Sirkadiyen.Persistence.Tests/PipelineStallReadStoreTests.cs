using Sirkadiyen.Application.Operations;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Operations.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// The stall queries, run against the real schema.
/// </summary>
/// <remarks>
/// These read nothing interesting on purpose. The failure mode worth catching
/// here is not a wrong count — it is a query PostgreSQL never sees, because the
/// value-object source id or the per-source "latest snapshot" subquery could not
/// be translated. That breaks at runtime, in the one component whose whole job is
/// to notice that something else broke, so it is proven against the database
/// rather than against a fake.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PipelineStallReadStoreTests(PostgresFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryStallQueryTranslatesAndRunsOnAnIdlePipeline()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        PipelineStallReadStore store = new(context);
        DateTimeOffset cutoff = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        StalledWork review = await store.CountRevisionsAwaitingReviewAsync(cutoff, Token);
        StalledWork unvalidated = await store.CountRevisionsStuckBeforeValidationAsync(
            cutoff,
            Token);
        StalledWork held = await store.CountDiffsAwaitingReleaseAsync(cutoff, Token);
        StalledWork failed = await store.CountFailedDispatchesAsync(Token);
        StalledWork unpolled = await store.CountSourcesNotPolledSinceAsync(cutoff, Token);

        // Whatever the shared fixture database holds, each read has to answer
        // without throwing, and a zero count must never carry an oldest item.
        foreach (StalledWork work in new[] { review, unvalidated, held, failed, unpolled })
        {
            Assert.True(work.Count >= 0);
            if (work.Count == 0)
            {
                Assert.Null(work.OldestSinceUtc);
                Assert.Null(work.OldestSourceId);
            }
            else
            {
                Assert.NotNull(work.OldestSourceId);
            }
        }
    }
}
