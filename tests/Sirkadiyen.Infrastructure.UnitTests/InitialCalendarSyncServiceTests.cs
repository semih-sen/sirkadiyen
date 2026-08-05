using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class InitialCalendarSyncServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task ItCreatesTheCalendarWritesApplicableEventsThenCompletes()
    {
        FakeConnectionStore connections = new(Pending(calendar: null));
        FakeCalendarClient client = new();
        FakeMappingStore mappings = new();
        CanonicalScheduleRecord first = CalendarTestData.Record(stableIdentity: "lesson-1");
        CanonicalScheduleRecord second = CalendarTestData.Record(stableIdentity: "lesson-2");

        InitialCalendarSyncService service = Build(
            connections,
            new FakeScheduleReadStore(first, second),
            mappings,
            client);

        InitialCalendarSyncRunResult run = await service.RunPendingAsync(CancellationToken.None);

        InitialCalendarSyncResult result = Assert.Single(run.Users);
        Assert.Equal(InitialCalendarSyncOutcome.Completed, result.Outcome);
        Assert.Equal(1, client.CalendarsCreated);
        Assert.Equal(2, client.Inserts.Count);
        Assert.Equal(2, mappings.Added.Count);
        Assert.True(connections.Completed);
        Assert.Equal("created-calendar-id", connections.AttachedCalendarId);
        Assert.All(client.Inserts, insert => Assert.Equal("created-calendar-id", insert.CalendarId));
    }

    [Fact]
    public async Task TheRefreshTokenIsDecryptedBeforeReachingTheClient()
    {
        FakeCalendarClient client = new();

        await Build(
            new FakeConnectionStore(Pending(calendar: null)),
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client).RunPendingAsync(CancellationToken.None);

        // The connection stores ciphertext; the client must see the decrypted token.
        Assert.NotEmpty(client.RefreshTokensSeen);
        Assert.All(client.RefreshTokensSeen, token => Assert.Equal("refresh-token", token));
    }

    [Fact]
    public async Task OnlyRecordsWithoutAMappingAreWritten()
    {
        FakeCalendarClient client = new();
        CanonicalScheduleRecord written = CalendarTestData.Record(stableIdentity: "already");
        CanonicalScheduleRecord fresh = CalendarTestData.Record(stableIdentity: "new-one");

        FakeConnectionStore connections = new(Pending(calendar: "cal"));
        InitialCalendarSyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore(written, fresh),
            new FakeMappingStore("already"),
            client).RunPendingAsync(CancellationToken.None));

        (string CalendarId, ManagedCalendarEvent Event) insert = Assert.Single(client.Inserts);
        Assert.Equal("new-one", insert.Event.PrivateProperties["stableIdentity"]);
        Assert.Equal(InitialCalendarSyncOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task AFrozenSystemWritesNothing()
    {
        FakeConnectionStore connections = new(Pending(calendar: null));
        FakeCalendarClient client = new();

        InitialCalendarSyncRunResult run = await Build(
            connections,
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client,
            frozen: true).RunPendingAsync(CancellationToken.None);

        Assert.True(run.Frozen);
        Assert.Empty(run.Users);
        Assert.Equal(0, client.CalendarsCreated);
        Assert.Empty(client.Inserts);
        Assert.False(connections.Completed);
    }

    [Fact]
    public async Task WorkOverTheCycleBudgetIsDeferredThenResumedUntilComplete()
    {
        FakeConnectionStore connections = new(Pending(calendar: null));
        FakeCalendarClient client = new();
        FakeMappingStore mappings = new();
        FakeScheduleReadStore schedule = new(
            CalendarTestData.Record(stableIdentity: "lesson-1"),
            CalendarTestData.Record(stableIdentity: "lesson-2"));
        InitialSyncOptions options = new() { EventsPerConnectionPerCycle = 1 };

        InitialCalendarSyncService service = Build(connections, schedule, mappings, client, options: options);

        InitialCalendarSyncResult firstPass = Single(await service.RunPendingAsync(CancellationToken.None));
        Assert.Equal(InitialCalendarSyncOutcome.InProgress, firstPass.Outcome);
        Assert.Single(client.Inserts);
        Assert.False(connections.Completed);

        InitialCalendarSyncResult secondPass = Single(await service.RunPendingAsync(CancellationToken.None));
        Assert.Equal(InitialCalendarSyncOutcome.Completed, secondPass.Outcome);
        Assert.Equal(2, client.Inserts.Count);
        // The calendar is created once and reused across cycles.
        Assert.Equal(1, client.CalendarsCreated);
        Assert.True(connections.Completed);
    }

    [Fact]
    public async Task ADuplicateInsertStillRecordsTheMappingAndCompletes()
    {
        FakeConnectionStore connections = new(Pending(calendar: "cal"));
        FakeMappingStore mappings = new();
        FakeCalendarClient client = new(insertOutcome: CalendarEventInsertOutcome.AlreadyExists);

        InitialCalendarSyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore(CalendarTestData.Record()),
            mappings,
            client).RunPendingAsync(CancellationToken.None));

        // A re-inserted event already exists, but the mapping is still recorded and the sync
        // completes — the idempotency the resume path depends on (ADR-058).
        Assert.Equal(InitialCalendarSyncOutcome.Completed, result.Outcome);
        Assert.Single(mappings.Added);
        Assert.True(connections.Completed);
    }

    [Fact]
    public async Task AnExistingCalendarIsReusedRatherThanRecreated()
    {
        FakeConnectionStore connections = new(Pending(calendar: "existing-cal"));
        FakeCalendarClient client = new();

        await Build(
            connections,
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client).RunPendingAsync(CancellationToken.None);

        Assert.Equal(0, client.CalendarsCreated);
        Assert.Equal(0, connections.AttachCount);
        Assert.All(client.Inserts, insert => Assert.Equal("existing-cal", insert.CalendarId));
    }

    [Fact]
    public async Task AnOrphanedMarkedCalendarIsRecoveredBeforeCreatingAnother()
    {
        FakeConnectionStore connections = new(Pending(calendar: null));
        FakeCalendarClient client = new();
        client.ExistingCalendars.Add("orphaned-calendar-id");

        InitialCalendarSyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client).RunPendingAsync(CancellationToken.None));

        Assert.Equal(InitialCalendarSyncOutcome.Completed, result.Outcome);
        Assert.Equal("orphaned-calendar-id", connections.AttachedCalendarId);
        Assert.Equal(0, client.CalendarsCreated);
        Assert.Equal(
            ManagedCalendarIdentity.DescriptionMarker(UserId),
            Assert.Single(client.MarkersSearched));
    }

    [Fact]
    public async Task SeveralMarkedCalendarsAreNotGuessedIntoAnAttachment()
    {
        FakeConnectionStore connections = new(Pending(calendar: null));
        FakeCalendarClient client = new();
        client.ExistingCalendars.AddRange(["first", "second"]);

        InitialCalendarSyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client).RunPendingAsync(CancellationToken.None));

        Assert.Equal(InitialCalendarSyncOutcome.Failed, result.Outcome);
        Assert.Null(connections.AttachedCalendarId);
        Assert.Equal(0, client.CalendarsCreated);
    }

    [Fact]
    public async Task CalendarCreationCarriesThePerUserRecoveryMarker()
    {
        FakeCalendarClient client = new();

        await Build(
            new FakeConnectionStore(Pending(calendar: null)),
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client).RunPendingAsync(CancellationToken.None);

        Assert.Equal(
            ManagedCalendarIdentity.DescriptionMarker(UserId),
            Assert.Single(client.MarkersCreated));
    }

    [Fact]
    public async Task ARejectedCredentialIsFlaggedAndStopsInitialSync()
    {
        FakeConnectionStore connections = new(Pending(calendar: null));
        FakeCalendarClient client = new()
        {
            FindFailure = new GoogleCalendarCredentialException("missing scope"),
        };

        InitialCalendarSyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client).RunPendingAsync(CancellationToken.None));

        Assert.Equal(InitialCalendarSyncOutcome.AuthorizationRequired, result.Outcome);
        Assert.True(connections.FlaggedForReauthorization);
        Assert.False(connections.Completed);
    }

    [Fact]
    public async Task RecordsForAnotherProgramAreNotWritten()
    {
        FakeCalendarClient client = new();

        InitialCalendarSyncResult result = Single(await Build(
            new FakeConnectionStore(Pending(calendar: "cal")),
            new FakeScheduleReadStore(
                CalendarTestData.Record(stableIdentity: "mine"),
                CalendarTestData.Record(classYear: 2, stableIdentity: "other-year")),
            new FakeMappingStore(),
            client).RunPendingAsync(CancellationToken.None));

        Assert.Equal(1, result.ApplicableRecordCount);
        (string CalendarId, ManagedCalendarEvent Event) insert = Assert.Single(client.Inserts);
        Assert.Equal("mine", insert.Event.PrivateProperties["stableIdentity"]);
    }

    [Fact]
    public async Task AMissingProfileIsReportedWithoutWriting()
    {
        FakeConnectionStore connections = new(Pending(calendar: null));
        FakeCalendarClient client = new();

        InitialCalendarSyncResult result = Single(await new InitialCalendarSyncService(
            connections,
            new FakeProfileStore(profile: null),
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client,
            new FakeTokenProtector(),
            new FakeFreezeStore(frozen: false),
            new InitialSyncOptions(),
            new FixedTimeProvider(Now),
            TestDepartmentColors.Create()).RunPendingAsync(CancellationToken.None));

        Assert.Equal(InitialCalendarSyncOutcome.ProfileMissing, result.Outcome);
        Assert.Equal(0, client.CalendarsCreated);
        Assert.Empty(client.Inserts);
        Assert.False(connections.Completed);
    }

    [Fact]
    public async Task ACalendarFailureIsIsolatedAndTheConnectionIsNotCompleted()
    {
        FakeConnectionStore connections = new(Pending(calendar: "cal"));
        FakeCalendarClient client = new(
            throwOnInsert: new GoogleCalendarSyncException("Google said no."));

        InitialCalendarSyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore(CalendarTestData.Record()),
            new FakeMappingStore(),
            client).RunPendingAsync(CancellationToken.None));

        Assert.Equal(InitialCalendarSyncOutcome.Failed, result.Outcome);
        Assert.Contains("Google said no.", result.FailureReason);
        Assert.False(connections.Completed);
    }

    private static InitialCalendarSyncService Build(
        FakeConnectionStore connections,
        FakeScheduleReadStore schedule,
        FakeMappingStore mappings,
        FakeCalendarClient client,
        bool frozen = false,
        InitialSyncOptions? options = null) =>
        new(
            connections,
            new FakeProfileStore(CalendarTestData.Profile()),
            schedule,
            mappings,
            client,
            new FakeTokenProtector(),
            new FakeFreezeStore(frozen),
            options ?? new InitialSyncOptions(),
            new FixedTimeProvider(Now),
            TestDepartmentColors.Create());

    private static PendingCalendarSync Pending(string? calendar) => new()
    {
        UserId = UserId,
        ProtectedRefreshToken = "protected:refresh-token",
        ManagedCalendarId = calendar,
    };

    private static InitialCalendarSyncResult Single(InitialCalendarSyncRunResult run) =>
        Assert.Single(run.Users);

    private sealed class FakeConnectionStore : IGoogleCalendarConnectionStore
    {
        private readonly List<PendingCalendarSync> pending;

        public FakeConnectionStore(params PendingCalendarSync[] pending) => this.pending = [.. pending];

        public string? AttachedCalendarId { get; private set; }

        public int AttachCount { get; private set; }

        public bool Completed { get; private set; }

        public bool FlaggedForReauthorization { get; private set; }

        public Task<IReadOnlyList<PendingCalendarSync>> ListPendingInitialSyncAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PendingCalendarSync>>([.. pending.Take(limit)]);

        public Task AttachManagedCalendarAsync(
            Guid userId,
            string managedCalendarId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            AttachedCalendarId = managedCalendarId;
            AttachCount++;
            for (int index = 0; index < pending.Count; index++)
            {
                if (pending[index].UserId == userId)
                {
                    pending[index] = pending[index] with { ManagedCalendarId = managedCalendarId };
                }
            }

            return Task.CompletedTask;
        }

        public Task MarkInitialSyncCompletedAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Completed = true;
            pending.RemoveAll(candidate => candidate.UserId == userId);
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

        public Task MarkNeedsReauthorizationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            FlaggedForReauthorization = true;
            return Task.CompletedTask;
        }

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

        public Task MarkCalendarInventoryCompletedAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkManagedCalendarUnavailableAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestReconciliationOutcome> RequestReconciliationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeCalendarClient(
        CalendarEventInsertOutcome insertOutcome = CalendarEventInsertOutcome.Inserted,
        Exception? throwOnInsert = null) : IUserCalendarClient
    {
        public int CalendarsCreated { get; private set; }

        public List<(string CalendarId, ManagedCalendarEvent Event)> Inserts { get; } = [];

        public List<string> RefreshTokensSeen { get; } = [];

        public List<string> ExistingCalendars { get; } = [];

        public List<string> MarkersSearched { get; } = [];

        public List<string> MarkersCreated { get; } = [];

        public Exception? FindFailure { get; init; }

        public Task<string> CreateManagedCalendarAsync(
            CalendarAccess access,
            string calendarSummary,
            string timeZoneId,
            string descriptionMarker,
            CancellationToken cancellationToken)
        {
            RefreshTokensSeen.Add(access.RefreshToken);
            MarkersCreated.Add(descriptionMarker);
            CalendarsCreated++;
            return Task.FromResult("created-calendar-id");
        }

        public Task<IReadOnlyList<string>> FindManagedCalendarIdsAsync(
            CalendarAccess access,
            string descriptionMarker,
            CancellationToken cancellationToken)
        {
            RefreshTokensSeen.Add(access.RefreshToken);
            MarkersSearched.Add(descriptionMarker);
            return FindFailure is null
                ? Task.FromResult<IReadOnlyList<string>>([.. ExistingCalendars])
                : throw FindFailure;
        }

        public Task<IReadOnlyList<ManagedCalendarEventSnapshot>> ListManagedEventsAsync(
            CalendarAccess access,
            string calendarId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task EnsureEventLabelAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEventLabel label,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CalendarEventInsertOutcome> InsertEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            RefreshTokensSeen.Add(access.RefreshToken);
            if (throwOnInsert is not null)
            {
                throw throwOnInsert;
            }

            Inserts.Add((calendarId, calendarEvent));
            return Task.FromResult(insertOutcome);
        }

        public Task<CalendarEventPatchOutcome> PatchEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarEventDeleteOutcome> DeleteEventAsync(
            CalendarAccess access,
            string calendarId,
            string eventId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeMappingStore(params string[] existing) : IUserCalendarEventMappingStore
    {
        private readonly HashSet<string> identities = new(existing, StringComparer.Ordinal);

        public List<UserCalendarEventMapping> Added { get; } = [];

        public Task<IReadOnlySet<string>> ListStableIdentitiesForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(identities, StringComparer.Ordinal));

        public Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(identities.Count);

        public Task<CalendarSyncProgressView> GetProgressForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEventMappingView>> ListForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarEventMappingAddOutcome> AddAsync(
            UserCalendarEventMapping mapping,
            CancellationToken cancellationToken)
        {
            Added.Add(mapping);
            bool added = identities.Add(mapping.StableIdentity);
            return Task.FromResult(
                added ? CalendarEventMappingAddOutcome.Added : CalendarEventMappingAddOutcome.AlreadyPresent);
        }

        public Task<IReadOnlyList<CalendarEventMappingView>> ListForStableIdentityAsync(
            SourceId sourceId,
            string stableIdentity,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateContentAsync(
            Guid userId,
            string stableIdentity,
            Guid canonicalRecordId,
            string contentHash,
            DateTimeOffset atUtc,
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

    private sealed class FakeScheduleReadStore(params CanonicalScheduleRecord[] records)
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

    private sealed class FakeProfileStore(StudentProfileView? profile) : IStudentProfileStore
    {
        public Task<StudentProfileView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StudentProfileUpsertResult> UpsertAsync(
            Guid userId,
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            string studentNumber,
            string selectorSchemaVersion,
            IReadOnlyDictionary<string, string> selectors,
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
