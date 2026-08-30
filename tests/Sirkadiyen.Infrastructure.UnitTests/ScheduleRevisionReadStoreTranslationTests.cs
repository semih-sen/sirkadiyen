using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Proves the review queue's projection is a query PostgreSQL can actually run (ADR-135).
/// </summary>
/// <remarks>
/// The projection joins the source and runs three correlated subqueries per revision, and none of
/// that is covered anywhere else: the store tests need a real database and are skipped unless one
/// is configured, which it is not in CI either. An untranslatable expression would therefore first
/// be discovered by an operator opening the review queue and getting a 500.
/// <para>
/// No database is needed to catch that. Entity Framework compiles and translates the query before
/// it opens a connection, so a query that fails to translate throws
/// <see cref="InvalidOperationException"/> while a translatable one gets as far as failing to
/// connect. Pointing the context at a port nothing listens on turns "did this translate" into an
/// assertion about which exception comes back.
/// </para>
/// </remarks>
public sealed class ScheduleRevisionReadStoreTranslationTests
{
    // Port 1 is privileged and unused; the connection attempt fails immediately rather than
    // waiting for a timeout.
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=sirkadiyen;Username=none;Password=none;Timeout=1";

    [Fact]
    public async Task TheReviewQueueProjectionTranslatesToSqlAsync() =>
        await AssertTranslatesAsync(store => store.ListByStateAsync(
            RevisionState.ReviewRequired,
            50,
            CancellationToken.None));

    [Fact]
    public async Task TheHistoryProjectionTranslatesToSqlAsync() =>
        await AssertTranslatesAsync(store => store.ListRecentAsync(
            50,
            sourceId: null,
            CancellationToken.None));

    [Fact]
    public async Task TheHistoryProjectionForOneSourceTranslatesToSqlAsync() =>
        await AssertTranslatesAsync(store => store.ListRecentAsync(
            50,
            "G1-TR-ANNUAL",
            CancellationToken.None));

    [Fact]
    public async Task TheDetailProjectionTranslatesToSqlAsync() =>
        await AssertTranslatesAsync(store => store.FindAsync(
            Guid.CreateVersion7(),
            CancellationToken.None));

    private static async Task AssertTranslatesAsync(Func<ScheduleRevisionReadStore, Task> query)
    {
        DbContextOptions<SirkadiyenDbContext> options =
            new DbContextOptionsBuilder<SirkadiyenDbContext>()
                .UseNpgsql(UnreachableDatabase)
                .Options;

        await using SirkadiyenDbContext dbContext = new(options);
        ScheduleRevisionReadStore store = new(dbContext);

        Exception thrown = await Record.ExceptionAsync(() => query(store))
            ?? throw new InvalidOperationException(
                "The query unexpectedly succeeded against an unreachable database.");

        // A translation failure is an InvalidOperationException naming the LINQ expression. Any
        // other exception means translation finished and the query only failed to reach a server,
        // which is what this test is asserting.
        Assert.False(
            thrown is InvalidOperationException && thrown.Message.Contains(
                "could not be translated",
                StringComparison.Ordinal),
            thrown.Message);
    }
}
