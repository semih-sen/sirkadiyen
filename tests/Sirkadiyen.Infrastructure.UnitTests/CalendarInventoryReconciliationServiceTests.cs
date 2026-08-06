using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class CalendarInventoryReconciliationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AMissingMappedEventIsRecreatedWithoutAnyDelete()
    {
        Harness harness = new();
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "missing");
        harness.Records.Add(record);
        harness.Mappings.Seed(Mapping(harness.UserId, record, "stored-event"));

        CalendarInventoryUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarInventoryOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.Inserted);
        Assert.Single(harness.Client.Patches);
        Assert.Single(harness.Client.Inserts);
        Assert.Empty(harness.Client.Deletes);
        Assert.True(harness.Connections.InventoryCompleted);
    }

    [Fact]
    public async Task AnUnledgeredMarkedEventIsAdoptedAndPatchedInPlace()
    {
        Harness harness = new();
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "orphan-event");
        harness.Records.Add(record);
        ManagedCalendarEvent expected =
            ManagedCalendarEventFactory.ToManagedEvent(harness.UserId, record) with
            {
                EventId = "google-generated-event",
                Summary = "user edited",
            };
        harness.Client.Events.Add(Snapshot(expected));

        CalendarInventoryUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarInventoryOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.Patched);
        Assert.Equal(1, result.MappingsRecovered);
        Assert.Empty(harness.Client.Inserts);
        CalendarEventMappingView mapping = Assert.Single(harness.Mappings.Items);
        Assert.Equal("google-generated-event", mapping.GoogleEventId);
        Assert.Empty(harness.Client.Deletes);
    }

    [Fact]
    public async Task VisibleUserEditsAreRepairedEvenWhenTheContentHashMarkerIsCurrent()
    {
        Harness harness = new();
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "edited");
        harness.Records.Add(record);
        CalendarEventMappingView mapping = Mapping(
            harness.UserId,
            record,
            ManagedCalendarEventFactory.DeterministicEventId(
                harness.UserId,
                record.StableIdentity));
        harness.Mappings.Seed(mapping);

        ManagedCalendarEvent expected =
            ManagedCalendarEventFactory.ToManagedEvent(harness.UserId, record);
        harness.Client.Events.Add(Snapshot(expected) with { Location = "Changed by user" });

        CalendarInventoryUserResult result = await harness.RunSingleAsync();

        Assert.Equal(1, result.Patched);
        Assert.Single(harness.Client.Patches);
        Assert.Empty(harness.Client.Deletes);
    }

    [Fact]
    public async Task AStalePresentationLabelIsRepairedWithoutACanonicalContentChange()
    {
        Harness harness = new();
        CanonicalScheduleRecord record = CalendarTestData.Record(
            stableIdentity: "old-color",
            departments: ["ANATOMİ AD."]);
        harness.Records.Add(record);
        ManagedCalendarEvent expected =
            ManagedCalendarEventFactory.ToManagedEvent(harness.UserId, record);
        harness.Mappings.Seed(Mapping(harness.UserId, record, expected.EventId));
        harness.Client.Events.Add(Snapshot(expected) with { EventLabelId = null });

        CalendarInventoryUserResult result = await harness.RunSingleAsync();

        Assert.Equal(1, result.Patched);
        ManagedCalendarEvent patch = Assert.Single(harness.Client.Patches);
        Assert.Equal("#D50000", patch.Label.BackgroundColor);
        Assert.Empty(harness.Client.Deletes);
    }

    [Fact]
    public async Task AChangedColorUpdatesTheCalendarLabelEvenWhenTheEventLabelIdIsCurrent()
    {
        Harness harness = new();
        CanonicalScheduleRecord record = CalendarTestData.Record(
            stableIdentity: "custom-color",
            departments: ["ANATOMİ AD."]);
        harness.Records.Add(record);
        ManagedCalendarEvent current =
            ManagedCalendarEventFactory.ToManagedEvent(harness.UserId, record);
        harness.Mappings.Seed(Mapping(harness.UserId, record, current.EventId));
        harness.Client.Events.Add(Snapshot(current));
        harness.Colors = new DepartmentColorService(
            new FixedDepartmentColorStore("anatomi", "#123456"),
            TimeProvider.System);

        CalendarInventoryUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarInventoryOutcome.Completed, result.Outcome);
        Assert.Empty(harness.Client.Patches);
        ManagedCalendarEventLabel label = Assert.Single(harness.Client.EnsuredLabels);
        Assert.Equal(current.Label.Id, label.Id);
        Assert.Equal("#123456", label.BackgroundColor);
    }

    [Fact]
    public async Task DuplicateAndUnexpectedStateIsReportedButNeverDeleted()
    {
        Harness harness = new();
        CanonicalScheduleRecord expected = CalendarTestData.Record(stableIdentity: "duplicate");
        CanonicalScheduleRecord stale = CalendarTestData.Record(stableIdentity: "stale");
        harness.Records.Add(expected);
        harness.Mappings.Seed(Mapping(harness.UserId, expected, "primary"));
        harness.Mappings.Seed(Mapping(harness.UserId, stale, "stale-event"));

        ManagedCalendarEvent managed =
            ManagedCalendarEventFactory.ToManagedEvent(harness.UserId, expected);
        harness.Client.Events.Add(Snapshot(managed with { EventId = "primary" }));
        harness.Client.Events.Add(Snapshot(managed with { EventId = "duplicate-copy" }));
        harness.Client.Events.Add(Snapshot(
            ManagedCalendarEventFactory.ToManagedEvent(harness.UserId, stale) with
            {
                EventId = "stale-event",
            }));

        CalendarInventoryUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarInventoryOutcome.CompletedWithConflicts, result.Outcome);
        Assert.Equal(1, result.Conflicts);
        Assert.Equal(1, result.UnexpectedMappings);
        Assert.Equal(1, result.UnexpectedEvents);
        Assert.Empty(harness.Client.Deletes);
    }

    [Fact]
    public async Task AnUnavailableManagedCalendarBecomesExplicitRepairRequired()
    {
        Harness harness = new();
        harness.Client.ListFailure =
            new GoogleManagedCalendarUnavailableException("calendar deleted");

        CalendarInventoryUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarInventoryOutcome.CalendarRepairRequired, result.Outcome);
        Assert.True(harness.Connections.CalendarUnavailable);
        Assert.False(harness.Connections.InventoryCompleted);
    }

    [Fact]
    public async Task ARejectedCredentialQueuesDiffReplayAndPreservesCalendarState()
    {
        Harness harness = new();
        harness.Client.ListFailure = new GoogleCalendarCredentialException("revoked");

        CalendarInventoryUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarInventoryOutcome.AuthorizationRequired, result.Outcome);
        Assert.True(harness.Connections.NeedsReauthorization);
        Assert.False(harness.Connections.CalendarUnavailable);
    }

    [Fact]
    public async Task AGlobalFreezeAdmitsNoInventoryWork()
    {
        Harness harness = new() { Frozen = true };

        CalendarInventoryRunResult result =
            await harness.Build().RunDueAsync(CancellationToken.None);

        Assert.True(result.Frozen);
        Assert.Empty(result.Users);
        Assert.Equal(0, harness.Targets.ListCalls);
    }

    private static CalendarEventMappingView Mapping(
        Guid userId,
        CanonicalScheduleRecord record,
        string eventId) => new()
        {
            UserId = userId,
            StableIdentity = record.StableIdentity,
            SourceId = record.SourceId,
            GoogleCalendarId = $"cal-{userId:N}",
            GoogleEventId = eventId,
            ContentHash = record.ContentHash,
            CanonicalRecordId = record.Id,
        };

    private static ManagedCalendarEventSnapshot Snapshot(ManagedCalendarEvent calendarEvent)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(calendarEvent.TimeZoneId);
        return new ManagedCalendarEventSnapshot
        {
            EventId = calendarEvent.EventId,
            Summary = calendarEvent.Summary,
            Description = calendarEvent.Description,
            Location = calendarEvent.Location,
            EventLabelId = calendarEvent.Label.Id,
            IsAllDay = calendarEvent.IsAllDay,
            StartDate = calendarEvent.StartDate,
            EndDateExclusive = calendarEvent.EndDateExclusive,
            StartAt = calendarEvent.LocalStart is { } start
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
                    zone.GetUtcOffset(start))
                : null,
            EndAt = calendarEvent.LocalEnd is { } end
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(end, DateTimeKind.Unspecified),
                    zone.GetUtcOffset(end))
                : null,
            PrivateProperties =
                new Dictionary<string, string>(
                    calendarEvent.PrivateProperties,
                    StringComparer.Ordinal),
        };
    }

    private sealed class Harness
    {
        public Harness()
        {
            UserId = Guid.CreateVersion7();
            Profile = CalendarTestData.Profile() with { UserId = UserId };
            Targets.Target = new CalendarInventoryTarget
            {
                UserId = UserId,
                ProtectedRefreshToken = "protected:token",
                ManagedCalendarId = $"cal-{UserId:N}",
                Profile = Profile,
            };
        }

        public Guid UserId { get; }

        public StudentProfileView Profile { get; }

        public bool Frozen { get; init; }

        public List<CanonicalScheduleRecord> Records { get; } = [];

        public FakeTargetStore Targets { get; } = new();

        public FakeMappingStore Mappings { get; } = new();

        public FakeConnectionStore Connections { get; } = new();

        public FakeCalendarClient Client { get; } = new();

        public DepartmentColorService Colors { get; set; } = TestDepartmentColors.Create();

        public CalendarInventoryReconciliationService Build() => new(
            Targets,
            new FakeScheduleReadStore(Records),
            Mappings,
            Connections,
            Client,
            new FakeTokenProtector(),
            new FakeFreezeStore(Frozen),
            new CalendarInventoryReconciliationOptions(),
            new FixedTimeProvider(Now),
            Colors);

        public async Task<CalendarInventoryUserResult> RunSingleAsync() =>
            Assert.Single((await Build().RunDueAsync(CancellationToken.None)).Users);
    }

    private sealed class FakeTargetStore : ICalendarSyncTargetReadStore
    {
        public CalendarInventoryTarget? Target { get; set; }

        public int ListCalls { get; private set; }

        public Task<IReadOnlyList<CalendarInventoryTarget>> ListInventoryTargetsAsync(
            DateTimeOffset dueBeforeUtc,
            int limit,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<CalendarInventoryTarget>>(
                Target is null ? [] : [Target]);
        }

        public Task<IReadOnlyList<CalendarSyncTarget>> ListCohortTargetsAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarSyncTarget>> ListTargetsByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeScheduleReadStore(IReadOnlyList<CanonicalScheduleRecord> records)
        : ICanonicalScheduleReadStore
    {
        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListCurrentPublishedRecordsAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CanonicalScheduleRecord>>([.. records]);

        public Task<IReadOnlyList<PublishedRecordIdentity>> ListCurrentPublishedIdentitiesAsync(
            string academicYear,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListRecordsByIdsAsync(
            IReadOnlyCollection<Guid> recordIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeMappingStore : IUserCalendarEventMappingStore
    {
        public List<CalendarEventMappingView> Items { get; } = [];

        public void Seed(CalendarEventMappingView mapping) => Items.Add(mapping);

        public Task<IReadOnlyList<CalendarEventMappingView>> ListForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEventMappingView>>(
                [.. Items.Where(mapping => mapping.UserId == userId)]);

        public Task<CalendarEventMappingAddOutcome> AddAsync(
            UserCalendarEventMapping mapping,
            CancellationToken cancellationToken)
        {
            if (Items.Any(item => item.UserId == mapping.UserId
                && string.Equals(item.StableIdentity, mapping.StableIdentity, StringComparison.Ordinal)))
            {
                return Task.FromResult(CalendarEventMappingAddOutcome.AlreadyPresent);
            }

            Items.Add(new CalendarEventMappingView
            {
                UserId = mapping.UserId,
                StableIdentity = mapping.StableIdentity,
                SourceId = mapping.SourceId,
                GoogleCalendarId = mapping.GoogleCalendarId,
                GoogleEventId = mapping.GoogleEventId,
                ContentHash = mapping.ContentHash,
                CanonicalRecordId = mapping.CanonicalRecordId,
            });
            return Task.FromResult(CalendarEventMappingAddOutcome.Added);
        }

        public Task UpdateContentAsync(
            Guid userId,
            string stableIdentity,
            Guid canonicalRecordId,
            string contentHash,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            int index = Items.FindIndex(item => item.UserId == userId
                && string.Equals(item.StableIdentity, stableIdentity, StringComparison.Ordinal));
            if (index >= 0)
            {
                Items[index] = Items[index] with
                {
                    CanonicalRecordId = canonicalRecordId,
                    ContentHash = contentHash,
                };
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<string>> ListStableIdentitiesForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> CountForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarSyncProgressView> GetProgressForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEventMappingView>> ListForStableIdentityAsync(
            SourceId sourceId,
            string stableIdentity,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarEventMappingReidentifyOutcome> ReidentifyAsync(
            Guid userId,
            SourceId sourceId,
            string previousStableIdentity,
            string currentStableIdentity,
            Guid canonicalRecordId,
            string contentHash,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarEventMappingRemoveOutcome> RemoveAsync(
            Guid userId,
            string stableIdentity,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeConnectionStore : ICalendarSyncConnectionStore
    {
        public bool InventoryCompleted { get; private set; }

        public bool CalendarUnavailable { get; private set; }

        public bool NeedsReauthorization { get; private set; }

        public Task MarkCalendarInventoryCompletedAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            InventoryCompleted = true;
            return Task.CompletedTask;
        }

        public Task MarkManagedCalendarUnavailableAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            CalendarUnavailable = true;
            return Task.CompletedTask;
        }

        public Task<RequestReconciliationOutcome> RequestReconciliationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkNeedsReauthorizationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            NeedsReauthorization = true;
            return Task.CompletedTask;
        }

        public Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GoogleCalendarConnectionView> UpsertAuthorizationAsync(
            Guid userId,
            string protectedRefreshToken,
            string grantedScopes,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestInitialSyncResult> RequestInitialSyncAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PendingCalendarSync>> ListPendingInitialSyncAsync(
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AttachManagedCalendarAsync(
            Guid userId,
            string managedCalendarId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkInitialSyncCompletedAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PendingProfileResync>> ListPendingProfileResyncAsync(
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CompleteProfileResyncOutcome> CompleteProfileResyncAsync(
            Guid userId,
            DateTimeOffset expectedRequiredSinceUtc,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PendingCalendarReconciliation>> ListPendingReconciliationAsync(
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AdvanceReconciliationCursorAsync(
            Guid userId,
            DateTimeOffset expectedRequiredSinceUtc,
            DateTimeOffset dispatchedAtUtc,
            Guid diffId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteReconciliationAsync(
            Guid userId,
            DateTimeOffset expectedRequiredSinceUtc,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeCalendarClient : IUserCalendarClient
    {
        public List<ManagedCalendarEventSnapshot> Events { get; } = [];

        public List<ManagedCalendarEvent> Inserts { get; } = [];

        public List<ManagedCalendarEvent> Patches { get; } = [];

        public List<ManagedCalendarEventLabel> EnsuredLabels { get; } = [];

        public List<string> Deletes { get; } = [];

        public Exception? ListFailure { get; set; }

        public Task<IReadOnlyList<ManagedCalendarEventSnapshot>> ListManagedEventsAsync(
            CalendarAccess access,
            string calendarId,
            CancellationToken cancellationToken) =>
            ListFailure is null
                ? Task.FromResult<IReadOnlyList<ManagedCalendarEventSnapshot>>([.. Events])
                : throw ListFailure;

        public Task<CalendarEventPatchOutcome> PatchEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            Patches.Add(calendarEvent);
            int index = Events.FindIndex(item =>
                string.Equals(item.EventId, calendarEvent.EventId, StringComparison.Ordinal));
            if (index < 0)
            {
                return Task.FromResult(CalendarEventPatchOutcome.NotFound);
            }

            Events[index] = Snapshot(calendarEvent);
            return Task.FromResult(CalendarEventPatchOutcome.Patched);
        }

        public Task EnsureEventLabelAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEventLabel label,
            CancellationToken cancellationToken)
        {
            EnsuredLabels.Add(label);
            return Task.CompletedTask;
        }

        public Task<CalendarEventInsertOutcome> InsertEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            Inserts.Add(calendarEvent);
            Events.Add(Snapshot(calendarEvent));
            return Task.FromResult(CalendarEventInsertOutcome.Inserted);
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
    }

    private sealed class FixedDepartmentColorStore(string key, string color)
        : IDepartmentColorStore
    {
        private static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>();

        public Task<IReadOnlyDictionary<string, string>> GetAdminDefaultsAsync(
            CancellationToken cancellationToken) => Task.FromResult(Empty);

        public Task<IReadOnlyDictionary<string, string>> GetUserOverridesAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> { [key] = color });

        public Task<bool> SetAdminDefaultAsync(
            string departmentKey,
            string? value,
            string actor,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> SetUserOverrideAsync(
            Guid userId,
            string departmentKey,
            string? value,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeTokenProtector : ICalendarTokenProtector
    {
        public string Protect(string plaintext) => $"protected:{plaintext}";

        public string Unprotect(string ciphertext) => ciphertext["protected:".Length..];
    }

    private sealed class FakeFreezeStore(bool frozen) : IOperationalFreezeStore
    {
        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = frozen });

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
