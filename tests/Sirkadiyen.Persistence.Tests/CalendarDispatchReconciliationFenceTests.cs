using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CalendarDispatchReconciliationFenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task OnlyOneWorkerOwnsTheFenceAndReleaseMakesItAvailableAgain()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        PostgresCalendarDispatchReconciliationFence first =
            new(fixture.ConnectionString!);
        PostgresCalendarDispatchReconciliationFence second =
            new(fixture.ConnectionString!);

        await using IAsyncDisposable? firstLease =
            await first.TryAcquireAsync(Token);
        Assert.NotNull(firstLease);
        Assert.Null(await second.TryAcquireAsync(Token));

        await firstLease.DisposeAsync();
        await using IAsyncDisposable? nextLease =
            await second.TryAcquireAsync(Token);
        Assert.NotNull(nextLease);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
