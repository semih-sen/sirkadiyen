using Sirkadiyen.Application.Meals;
using Sirkadiyen.Domain.Meals;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The acquisition rules that keep menus safe (ADR-150): a first sighting is created, a changed menu
/// bumps its version, a transport failure is never a miss, and only repeated misses withdraw a day.
/// </summary>
public sealed class MealMenuAcquisitionServiceTests
{
    // 09:00 UTC on 2026-09-06 is still 2026-09-06 in Istanbul, so the single-day window is that date.
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 9, 6);

    private static MealMenuOptions Options(int windowDays = 0, int threshold = 3) => new()
    {
        Category = MealCategory.Lunch,
        WindowDays = windowDays,
        WithdrawalMissThreshold = threshold,
        TimeZoneId = "Europe/Istanbul",
    };

    [Fact]
    public async Task AFirstSightingIsCreatedAsPublished()
    {
        FakeApi api = new(_ => MealMenuFetchResult.Found("Çorba,\r\nKöfte"));
        FakeStore store = new();

        MealAcquisitionResult result =
            await Service(api, store, Options()).AcquireAsync(CancellationToken.None);

        Assert.Equal(1, result.Published);
        MealMenuDay day = Assert.Single(store.Days);
        Assert.Equal(MealMenuDayStatus.Published, day.Status);
        Assert.Equal("Çorba\nKöfte", day.MealText);
    }

    [Fact]
    public async Task AChangedMenuBumpsTheVersion()
    {
        FakeStore store = new();
        store.Days.Add(MealMenuDay.CreatePublished(
            Date, MealCategory.Lunch, "Çorba", MealMenuText.Hash("Çorba"), Now.AddDays(-1)));
        FakeApi api = new(_ => MealMenuFetchResult.Found("Pilav"));

        MealAcquisitionResult result =
            await Service(api, store, Options()).AcquireAsync(CancellationToken.None);

        Assert.Equal(1, result.ContentChanged);
        Assert.Equal(2, store.Days[0].ContentVersion);
        Assert.Equal("Pilav", store.Days[0].MealText);
    }

    [Fact]
    public async Task ATransportFailureIsRecordedAsAnErrorNotAMiss()
    {
        FakeStore store = new();
        MealMenuDay existing = MealMenuDay.CreatePublished(
            Date, MealCategory.Lunch, "Çorba", MealMenuText.Hash("Çorba"), Now.AddDays(-1));
        store.Days.Add(existing);
        FakeApi api = new(_ => throw new HttpRequestException("the cafeteria API is down"));

        MealAcquisitionResult result =
            await Service(api, store, Options()).AcquireAsync(CancellationToken.None);

        Assert.Equal(1, result.ApiErrors);
        Assert.Equal(0, result.Missed);
        // The day is untouched: an outage must never withdraw a month of menus.
        Assert.Equal(0, existing.ConsecutiveMissCount);
        Assert.Equal(MealMenuDayStatus.Published, existing.Status);
    }

    [Fact]
    public async Task OnlyRepeatedEmptyAnswersWithdrawAPreviouslyPublishedDay()
    {
        FakeStore store = new();
        store.Days.Add(MealMenuDay.CreatePublished(
            Date, MealCategory.Lunch, "Çorba", MealMenuText.Hash("Çorba"), Now.AddDays(-1)));
        FakeApi api = new(_ => MealMenuFetchResult.NotFound);
        MealMenuAcquisitionService service = Service(api, store, Options(threshold: 2));

        await service.AcquireAsync(CancellationToken.None);
        Assert.Equal(MealMenuDayStatus.Published, store.Days[0].Status);

        MealAcquisitionResult second = await service.AcquireAsync(CancellationToken.None);
        Assert.Equal(MealMenuDayStatus.Withdrawn, store.Days[0].Status);
        Assert.Equal(1, second.Withdrawn);
    }

    private static MealMenuAcquisitionService Service(
        FakeApi api,
        FakeStore store,
        MealMenuOptions options) =>
        new(api, store, options, new FixedTimeProvider(Now));

    private sealed class FakeApi(Func<DateOnly, MealMenuFetchResult> responder) : IMealMenuApiClient
    {
        public Task<MealMenuFetchResult> FetchAsync(
            DateOnly date,
            MealCategory category,
            CancellationToken cancellationToken) => Task.FromResult(responder(date));
    }

    private sealed class FakeStore : IMealMenuStore
    {
        public List<MealMenuDay> Days { get; } = [];

        public Task<IReadOnlyList<MealMenuDay>> ListForWindowAsync(
            MealCategory category,
            DateOnly fromInclusive,
            DateOnly toInclusive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MealMenuDay>>(
            [
                .. Days.Where(day => day.Category == category
                    && day.LocalDate >= fromInclusive
                    && day.LocalDate <= toInclusive),
            ]);

        public Task PersistAsync(
            IReadOnlyCollection<MealMenuDay> newDays,
            IReadOnlyCollection<MealMenuDay> mutatedDays,
            CancellationToken cancellationToken)
        {
            Days.AddRange(newDays);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
