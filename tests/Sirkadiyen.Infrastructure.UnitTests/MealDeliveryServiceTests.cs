using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Meals;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Meals;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The convergence half of the cafeteria menu (ADR-150): the freeze gate, the idempotent write, the
/// patch of a stale copy, the eligibility skip, and the removal of a copy no longer owed.
/// </summary>
public sealed class MealDeliveryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 9, 6);

    [Fact]
    public async Task AGlobalFreezeWritesNothing()
    {
        Harness harness = new() { Frozen = true };
        harness.WriteTargets.Add(PendingTarget());

        MealDeliveryRunResult result = await harness.Build().RunAsync(CancellationToken.None);

        Assert.True(result.Frozen);
        Assert.Empty(harness.Client.Inserts);
    }

    [Fact]
    public async Task AnOwedCopyIsInsertedOnceAndMarkedWritten()
    {
        Harness harness = new();
        harness.WriteTargets.Add(PendingTarget());

        MealDeliveryRunResult result = await harness.Build().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.EventsWritten);
        ManagedCalendarEvent inserted = Assert.Single(harness.Client.Inserts);
        Assert.Equal(
            ManagedCalendarEventFactory.MealKind,
            inserted.PrivateProperties[ManagedCalendarEventFactory.KindKey]);
        Assert.Single(harness.Store.Written);
    }

    [Fact]
    public async Task AStaleWrittenCopyIsPatchedRatherThanInserted()
    {
        Harness harness = new();
        harness.WriteTargets.Add(PendingTarget() with
        {
            GoogleEventId = "evt",
            AppliedContentVersion = 1,
            ContentVersion = 2,
        });

        MealDeliveryRunResult result = await harness.Build().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.EventsPatched);
        Assert.Empty(harness.Client.Inserts);
        Assert.Single(harness.Client.Patches);
    }

    [Fact]
    public async Task AnIneligibleSubscriberIsSkippedWithoutTouchingTheCalendar()
    {
        Harness harness = new();
        harness.WriteTargets.Add(PendingTarget() with
        {
            CurrentExclusion = MealDeliveryExclusionReason.CalendarAuthorizationRevoked,
            ProtectedRefreshToken = null,
        });

        MealDeliveryRunResult result = await harness.Build().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.SubscribersSkipped);
        Assert.Empty(harness.Client.Inserts);
        Assert.Equal(
            MealDeliveryExclusionReason.CalendarAuthorizationRevoked,
            Assert.Single(harness.Store.Skipped).Reason);
    }

    [Fact]
    public async Task AWithdrawnDayHasItsWrittenCopyRemoved()
    {
        Harness harness = new();
        harness.RemovalTargets.Add(new MealDeliveryRemovalTarget
        {
            DeliveryId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            GoogleEventId = "evt",
            ManagedCalendarId = "cal",
            ProtectedRefreshToken = "protected:token",
        });

        MealDeliveryRunResult result = await harness.Build().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.EventsRemoved);
        Assert.Single(harness.Client.Deletes);
        Assert.Single(harness.Store.Removed);
    }

    [Fact]
    public async Task ACopyThatWasNeverWrittenIsRetiredWithoutACalendarCall()
    {
        Harness harness = new();
        harness.RemovalTargets.Add(new MealDeliveryRemovalTarget
        {
            DeliveryId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            GoogleEventId = null,
            ManagedCalendarId = null,
            ProtectedRefreshToken = null,
        });

        MealDeliveryRunResult result = await harness.Build().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.EventsRemoved);
        Assert.Empty(harness.Client.Deletes);
        Assert.Single(harness.Store.Removed);
    }

    private static MealDeliveryWriteTarget PendingTarget() => new()
    {
        DeliveryId = Guid.CreateVersion7(),
        UserId = Guid.CreateVersion7(),
        LocalDate = Date,
        Category = MealCategory.Lunch,
        MealText = "Çorba\nKöfte",
        ContentVersion = 1,
        ProtectedRefreshToken = "protected:token",
        ManagedCalendarId = "cal",
        CurrentExclusion = null,
        GoogleEventId = null,
        AppliedContentVersion = null,
    };

    private sealed class Harness
    {
        public bool Frozen { get; init; }

        public FakeStore Store { get; } = new();

        public FakeCalendarClient Client { get; } = new();

        public List<MealDeliveryWriteTarget> WriteTargets => Store.WriteTargets;

        public List<MealDeliveryRemovalTarget> RemovalTargets => Store.RemovalTargets;

        public MealDeliveryService Build() => new(
            Store,
            Client,
            new FakeTokenProtector(),
            new FakeConnectionHealthWriter(),
            new FakeFreezeStore(Frozen),
            TestDepartmentColors.Create(),
            new MealMenuOptions { TimeZoneId = "Europe/Istanbul" },
            new FixedTimeProvider(Now));
    }

    private sealed class FakeStore : IMealDeliveryStore
    {
        public List<MealDeliveryWriteTarget> WriteTargets { get; } = [];

        public List<MealDeliveryRemovalTarget> RemovalTargets { get; } = [];

        public List<Guid> Written { get; } = [];

        public List<(Guid DeliveryId, MealDeliveryExclusionReason Reason)> Skipped { get; } = [];

        public List<Guid> Removed { get; } = [];

        public Task<int> ReconcileOwedAsync(
            MealCategory category,
            DateOnly fromInclusive,
            DateOnly toInclusive,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyList<MealDeliveryWriteTarget>> ListWriteTargetsAsync(
            MealCategory category,
            DateOnly fromInclusive,
            DateOnly toInclusive,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MealDeliveryWriteTarget>>([.. WriteTargets.Take(limit)]);

        public Task<IReadOnlyList<MealDeliveryRemovalTarget>> ListRemovalTargetsAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MealDeliveryRemovalTarget>>([.. RemovalTargets.Take(limit)]);

        public Task MarkWrittenAsync(
            Guid deliveryId,
            string googleEventId,
            int contentVersion,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Written.Add(deliveryId);
            return Task.CompletedTask;
        }

        public Task MarkSkippedAsync(
            Guid deliveryId,
            MealDeliveryExclusionReason reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Skipped.Add((deliveryId, reason));
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid deliveryId,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkRemovedAsync(
            Guid deliveryId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Removed.Add(deliveryId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCalendarClient : IUserCalendarClient
    {
        public List<ManagedCalendarEvent> Inserts { get; } = [];

        public List<ManagedCalendarEvent> Patches { get; } = [];

        public List<string> Deletes { get; } = [];

        public Task<CalendarEventInsertOutcome> InsertEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            Inserts.Add(calendarEvent);
            return Task.FromResult(CalendarEventInsertOutcome.Inserted);
        }

        public Task<CalendarEventPatchOutcome> PatchEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            Patches.Add(calendarEvent);
            return Task.FromResult(CalendarEventPatchOutcome.Patched);
        }

        public Task<CalendarEventDeleteOutcome> DeleteEventAsync(
            CalendarAccess access,
            string calendarId,
            string eventId,
            CancellationToken cancellationToken)
        {
            Deletes.Add(eventId);
            return Task.FromResult(CalendarEventDeleteOutcome.Deleted);
        }

        public Task<CalendarContainerDeleteOutcome> DeleteManagedCalendarAsync(
            CalendarAccess access,
            string calendarId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> CreateManagedCalendarAsync(
            CalendarAccess access,
            string calendarSummary,
            string timeZoneId,
            string descriptionMarker,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> FindManagedCalendarIdsAsync(
            CalendarAccess access,
            string descriptionMarker,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ManagedCalendarEventSnapshot>> ListManagedEventsAsync(
            CalendarAccess access,
            string calendarId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task EnsureEventLabelAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEventLabel label,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeTokenProtector : ICalendarTokenProtector
    {
        public string Protect(string plaintext) => $"protected:{plaintext}";

        public string Unprotect(string ciphertext) => ciphertext["protected:".Length..];
    }

    private sealed class FakeConnectionHealthWriter : ICalendarConnectionHealthWriter
    {
        public Task MarkNeedsReauthorizationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkManagedCalendarUnavailableAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFreezeStore(bool frozen) : IOperationalFreezeStore
    {
        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = frozen });

        public Task<OperationalFreezeSnapshot> GetScopedAsync(
            OperationalFreezeScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = false, Scope = scope });

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool isFrozen,
            string changedBy,
            string reason,
            string correlationId,
            DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
