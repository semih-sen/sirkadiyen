using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The delivery half of an announcement (ADR-107): the freeze gate, the idempotent write, the
/// per-recipient skip, the budget yield, the back-off, and the cancellation that removes what was
/// written.
/// </summary>
public sealed class AnnouncementDispatchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AGlobalFreezeWritesNothingAndLeavesEveryDeliveryQueued()
    {
        Harness harness = new() { Frozen = true };
        harness.SeedPending(2);

        AnnouncementDispatchRunResult run =
            await harness.Build().RunPendingAsync(CancellationToken.None);

        Assert.True(run.Frozen);
        Assert.Empty(run.Announcements);
        Assert.Empty(harness.Client.Inserts);
        Assert.Equal(0, harness.Store.DispatchableCalls);
    }

    [Fact]
    public async Task EveryEligibleRecipientIsWrittenOnceAndTheCampaignCompletes()
    {
        Harness harness = new();
        harness.SeedPending(3);

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        Assert.Equal(AnnouncementDispatchOutcome.Completed, result.Outcome);
        Assert.Equal(3, result.EventsWritten);
        Assert.Equal(3, harness.Client.Inserts.Count);
        Assert.Empty(harness.Client.Deletes);
        Assert.Equal(3, harness.Store.Written.Count);

        // Each recipient's id is derived from their own account, so no two share an event id.
        Assert.Equal(3, harness.Client.Inserts.Select(insert => insert.EventId).Distinct().Count());
        Assert.Equal(
            AnnouncementDispatchTransition.Completed,
            harness.Store.Transitions[^1]);
    }

    [Fact]
    public async Task AnAlreadyPresentEventIsPatchedRatherThanCountedAsAFailure()
    {
        Harness harness = new();
        harness.SeedPending(1);
        harness.Client.InsertOutcome = CalendarEventInsertOutcome.AlreadyExists;

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        // A crash between the Calendar write and the ledger row is always possible, so a re-run
        // must converge rather than report an error.
        Assert.Equal(AnnouncementDispatchOutcome.Completed, result.Outcome);
        Assert.Single(harness.Client.Patches);
        Assert.Single(harness.Store.Written);
    }

    [Fact]
    public async Task ARecipientWhoBecameIneligibleIsSkippedWithTheReasonAndNotWrittenTo()
    {
        Harness harness = new();
        harness.SeedPending(1);
        harness.SeedPending(1, exclusion: AnnouncementExclusionReason.LicenseInactive);

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        Assert.Equal(AnnouncementDispatchOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.EventsWritten);
        Assert.Equal(1, result.RecipientsSkipped);
        Assert.Single(harness.Client.Inserts);
        Assert.Equal(
            AnnouncementExclusionReason.LicenseInactive,
            Assert.Single(harness.Store.Skipped).Reason);
    }

    [Fact]
    public async Task ADeadCredentialSkipsThatRecipientAndFlagsTheirConnectionOnce()
    {
        Harness harness = new();
        harness.SeedPending(1);
        harness.SeedPending(1);
        harness.Client.FailFirstInsertWith = new GoogleCalendarCredentialException("revoked");

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        // One recipient's dead grant must not stop the campaign for everyone else.
        Assert.Equal(AnnouncementDispatchOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.EventsWritten);
        Assert.Equal(1, result.RecipientsSkipped);
        Assert.True(harness.Connections.FlaggedForReauthorization);
        Assert.Equal(
            AnnouncementExclusionReason.CalendarAuthorizationRevoked,
            Assert.Single(harness.Store.Skipped).Reason);
    }

    [Fact]
    public async Task AScopedFreezeLeavesThoseRecipientsQueuedRatherThanSkippingThem()
    {
        Harness harness = new() { ScopedFrozen = true };
        harness.SeedPending(2);

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        // Frozen is not the same as ineligible: the recipient is still owed the announcement, so
        // the row stays pending and the pass after the thaw picks it up.
        Assert.Equal(AnnouncementDispatchOutcome.InProgress, result.Outcome);
        Assert.Equal(2, result.RecipientsDeferred);
        Assert.Empty(harness.Client.Inserts);
        Assert.Empty(harness.Store.Skipped);
        Assert.Contains(AnnouncementDispatchTransition.DeferredForBudget, harness.Store.Transitions);
    }

    [Fact]
    public async Task ReachingThePerCycleBudgetYieldsWithoutCountingAsAFailure()
    {
        Harness harness = new()
        {
            Options = new AnnouncementDispatchOptions
            {
                CalendarOperationsPerAnnouncementPerCycle = 2,
            },
        };
        harness.SeedPending(5);

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        Assert.Equal(AnnouncementDispatchOutcome.InProgress, result.Outcome);
        Assert.Equal(2, result.EventsWritten);
        Assert.Equal(2, harness.Client.Inserts.Count);
        Assert.DoesNotContain(AnnouncementDispatchTransition.Completed, harness.Store.Transitions);
        Assert.Null(harness.Store.DeferredUntil);
    }

    [Fact]
    public async Task ATransientProviderFailureDefersTheWholePassWithABackOff()
    {
        Harness harness = new();
        harness.SeedPending(3);
        harness.Client.FailFirstInsertWith = new GoogleCalendarTransientException("rate limited");

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        // Stopping the pass is the point: burning the remaining recipients through the same rate
        // limit would turn one deferral into many failures.
        Assert.Equal(AnnouncementDispatchOutcome.Deferred, result.Outcome);
        Assert.Empty(harness.Client.Inserts);
        Assert.NotNull(harness.Store.DeferredUntil);
        Assert.True(harness.Store.DeferredUntil > Now);
    }

    [Fact]
    public async Task TheAttemptCapStopsAnAnnouncementInsteadOfRetryingForever()
    {
        Harness harness = new()
        {
            Options = new AnnouncementDispatchOptions { MaximumDeliveryAttempts = 3 },
        };
        harness.SeedPending(1, attempts: 2);
        harness.Client.FailFirstInsertWith = new GoogleCalendarTransientException("still failing");

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        Assert.Equal(AnnouncementDispatchOutcome.Failed, result.Outcome);
        Assert.Equal("still failing", harness.Store.RunFailureReason);
        Assert.Null(harness.Store.DeferredUntil);
    }

    [Fact]
    public async Task CancellingRemovesEveryWrittenCopyAndKeepsTheDeliveryRows()
    {
        Harness harness = new();
        harness.SeedWritten(2);
        harness.Status = CalendarAnnouncementStatus.Cancelling;

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        Assert.Equal(AnnouncementDispatchOutcome.Cancelled, result.Outcome);
        Assert.Equal(2, result.EventsRemoved);
        Assert.Equal(2, harness.Client.Deletes.Count);
        Assert.Equal(2, harness.Store.Removed.Count);
        Assert.Contains(AnnouncementDispatchTransition.Cancelled, harness.Store.Transitions);
    }

    [Fact]
    public async Task ACopyThatCannotBeReachedIsReportedRatherThanClaimedRemoved()
    {
        Harness harness = new();
        harness.SeedWritten(1, withCredential: false);
        harness.Status = CalendarAnnouncementStatus.Cancelling;

        AnnouncementDispatchResult result = await harness.RunSingleAsync();

        // The event is still on the student's calendar, so the row must not say it was removed.
        Assert.Equal(1, result.DeliveriesFailed);
        Assert.Equal(0, result.EventsRemoved);
        Assert.Empty(harness.Client.Deletes);
        Assert.Empty(harness.Store.Removed);
    }

    private sealed class Harness
    {
        private readonly List<AnnouncementDeliveryTarget> pending = [];

        private readonly List<AnnouncementDeliveryTarget> written = [];

        public Guid AnnouncementId { get; } = Guid.CreateVersion7();

        public bool Frozen { get; init; }

        public bool ScopedFrozen { get; init; }

        public CalendarAnnouncementStatus Status { get; set; } =
            CalendarAnnouncementStatus.Queued;

        public int Attempts { get; private set; }

        public AnnouncementDispatchOptions Options { get; init; } = new();

        public FakeAnnouncementStore Store { get; private set; } = null!;

        public FakeConnectionHealthWriter Connections { get; } = new();

        public FakeCalendarClient Client { get; } = new();

        public void SeedPending(
            int count,
            AnnouncementExclusionReason? exclusion = null,
            int attempts = 0)
        {
            Attempts = attempts;
            for (int index = 0; index < count; index++)
            {
                Guid userId = Guid.CreateVersion7();
                pending.Add(new AnnouncementDeliveryTarget
                {
                    DeliveryId = Guid.CreateVersion7(),
                    UserId = userId,
                    ProtectedRefreshToken = exclusion is null ? "protected:token" : null,
                    ManagedCalendarId = exclusion is null ? $"cal-{userId:N}" : null,
                    ClassYear = 2,
                    ProgramLanguage = ProgramLanguage.Turkish,
                    CurrentExclusion = exclusion,
                });
            }
        }

        public void SeedWritten(int count, bool withCredential = true)
        {
            for (int index = 0; index < count; index++)
            {
                Guid userId = Guid.CreateVersion7();
                written.Add(new AnnouncementDeliveryTarget
                {
                    DeliveryId = Guid.CreateVersion7(),
                    UserId = userId,
                    ProtectedRefreshToken = withCredential ? "protected:token" : null,
                    ManagedCalendarId = $"cal-{userId:N}",
                    ClassYear = 2,
                    ProgramLanguage = ProgramLanguage.Turkish,
                    CurrentExclusion = withCredential
                        ? null
                        : AnnouncementExclusionReason.CalendarAuthorizationRevoked,
                    GoogleEventId = $"event-{userId:N}",
                    AppliedContentVersion = 1,
                });
            }
        }

        public AnnouncementDispatchService Build()
        {
            Store = new FakeAnnouncementStore(
                Candidate(),
                pending,
                written);
            return new AnnouncementDispatchService(
                Store,
                Connections,
                Client,
                new FakeTokenProtector(),
                new FakeFreezeStore(Frozen, ScopedFrozen),
                Options,
                new FixedTimeProvider(Now));
        }

        public async Task<AnnouncementDispatchResult> RunSingleAsync() =>
            Assert.Single((await Build().RunPendingAsync(CancellationToken.None)).Announcements);

        private AnnouncementDispatchCandidate Candidate() => new()
        {
            AnnouncementId = AnnouncementId,
            Kind = CalendarAnnouncementKind.Bulk,
            Status = Status,
            ContentVersion = 1,
            DeliveryAttempts = Attempts,
            Title = "Telafi dersi",
            Body = "Gövde",
            IsAllDay = false,
            LocalDate = new DateOnly(2026, 11, 12),
            StartLocalTime = new TimeOnly(9, 0),
            EndLocalTime = new TimeOnly(10, 0),
            TimeZoneId = AnnouncementService.TimeZoneId,
            CategoryKey = AnnouncementCategoryCatalog.DefaultKey,
        };
    }

    private sealed class FakeAnnouncementStore(
        AnnouncementDispatchCandidate candidate,
        IReadOnlyList<AnnouncementDeliveryTarget> pending,
        IReadOnlyList<AnnouncementDeliveryTarget> written) : IAnnouncementStore
    {
        public int DispatchableCalls { get; private set; }

        public List<AnnouncementDispatchTransition> Transitions { get; } = [];

        public List<Guid> Written { get; } = [];

        public List<(Guid DeliveryId, AnnouncementExclusionReason Reason)> Skipped { get; } = [];

        public List<Guid> Removed { get; } = [];

        public List<(Guid DeliveryId, string Reason)> Failed { get; } = [];

        public DateTimeOffset? DeferredUntil { get; private set; }

        public string? RunFailureReason { get; private set; }

        public Task<IReadOnlyList<AnnouncementDispatchCandidate>> ListDispatchableAsync(
            DateTimeOffset nowUtc,
            int limit,
            CancellationToken cancellationToken)
        {
            DispatchableCalls++;
            return Task.FromResult<IReadOnlyList<AnnouncementDispatchCandidate>>([candidate]);
        }

        public Task<IReadOnlyList<AnnouncementDeliveryTarget>> ListDeliveryTargetsAsync(
            Guid announcementId,
            CalendarAnnouncementDeliveryState state,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnnouncementDeliveryTarget>>(
                state is CalendarAnnouncementDeliveryState.Pending
                    ? [.. pending.Take(limit)]
                    : [.. written.Take(limit)]);

        public Task MarkDeliveryWrittenAsync(
            Guid deliveryId,
            string googleEventId,
            int contentVersion,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Written.Add(deliveryId);
            return Task.CompletedTask;
        }

        public Task MarkDeliverySkippedAsync(
            Guid deliveryId,
            AnnouncementExclusionReason reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Skipped.Add((deliveryId, reason));
            return Task.CompletedTask;
        }

        public Task MarkDeliveryFailedAsync(
            Guid deliveryId,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Failed.Add((deliveryId, reason));
            return Task.CompletedTask;
        }

        public Task MarkDeliveryRemovedAsync(
            Guid deliveryId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Removed.Add(deliveryId);
            return Task.CompletedTask;
        }

        public Task ApplyDispatchOutcomeAsync(
            Guid announcementId,
            AnnouncementDispatchTransition transition,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Transitions.Add(transition);
            return Task.CompletedTask;
        }

        public Task DeferAfterFailureAsync(
            Guid announcementId,
            string reason,
            DateTimeOffset nextAttemptAtUtc,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            DeferredUntil = nextAttemptAtUtc;
            return Task.CompletedTask;
        }

        public Task MarkDeliveryRunFailedAsync(
            Guid announcementId,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            RunFailureReason = reason;
            return Task.CompletedTask;
        }

        public Task<AnnouncementSummary?> FindByCampaignKeyAsync(
            string campaignKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AnnouncementCreateStoreResult> AddAsync(
            CalendarAnnouncement announcement,
            IReadOnlyList<CalendarAnnouncementDelivery> deliveries,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AnnouncementSummary>> ListAsync(
            CalendarAnnouncementKind? kind,
            CalendarAnnouncementStatus? status,
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AnnouncementDetail?> FindAsync(
            Guid announcementId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Application.Common.PagedResult<AnnouncementDeliveryView>> ListDeliveriesAsync(
            Guid announcementId,
            CalendarAnnouncementDeliveryState? state,
            int page,
            int pageSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UpdateAnnouncementResult> UpdateContentAsync(
            Guid announcementId,
            AnnouncementContent content,
            string updatedBy,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CancelAnnouncementResult> RequestCancellationAsync(
            Guid announcementId,
            string cancelledBy,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeConnectionHealthWriter : ICalendarConnectionHealthWriter
    {
        public bool FlaggedForReauthorization { get; private set; }

        public bool FlaggedCalendarUnavailable { get; private set; }

        public Task MarkNeedsReauthorizationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            FlaggedForReauthorization = true;
            return Task.CompletedTask;
        }

        public Task MarkManagedCalendarUnavailableAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            FlaggedCalendarUnavailable = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCalendarClient : IUserCalendarClient
    {
        private bool firstInsertAttempted;

        public List<ManagedCalendarEvent> Inserts { get; } = [];

        public List<ManagedCalendarEvent> Patches { get; } = [];

        public List<string> Deletes { get; } = [];

        public CalendarEventInsertOutcome InsertOutcome { get; set; } =
            CalendarEventInsertOutcome.Inserted;

        /// <summary>Thrown on the first insert only, so the pass's own handling is what is tested.</summary>
        public Exception? FailFirstInsertWith { get; set; }

        public Task<CalendarEventInsertOutcome> InsertEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            if (FailFirstInsertWith is { } failure && !firstInsertAttempted)
            {
                firstInsertAttempted = true;
                throw failure;
            }

            firstInsertAttempted = true;
            Inserts.Add(calendarEvent);
            return Task.FromResult(InsertOutcome);
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

    private sealed class FakeFreezeStore(bool frozen, bool scopedFrozen) : IOperationalFreezeStore
    {
        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = frozen });

        public Task<OperationalFreezeSnapshot> GetScopedAsync(
            OperationalFreezeScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot
            {
                IsFrozen = scopedFrozen,
                Scope = scope,
            });

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
