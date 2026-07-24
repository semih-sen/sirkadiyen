using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The worker stage that fans a dispatchable diff out onto student calendars (ADR-059), exercised
/// against fakes up to the Google boundary.
/// </summary>
public sealed class IncrementalCalendarSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly SourceId Source = SourceId.Parse("G1-TR-ANNUAL");

    [Fact]
    public async Task ItInsertsACreatedLessonForEveryMatchingCohortUser()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-1");
        Guid user1 = Guid.CreateVersion7();
        Guid user2 = Guid.CreateVersion7();

        Harness harness = new();
        harness.Records.Add(record);
        harness.Cohort.Add(Target(user1, "t1"));
        harness.Cohort.Add(Target(user2, "t2"));
        harness.Diffs.Add(Diff(Created(record.Id)));

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        Assert.Equal(IncrementalDispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(2, harness.Client.Inserts.Count);
        Assert.Equal(2, harness.Mappings.Added.Count);
        Assert.True(harness.Diffs.WasDispatched);
    }

    [Fact]
    public async Task AnAlreadyMappedCohortUserIsNotInsertedAgain()
    {
        // The idempotent-replay case: everyone applicable already holds the lesson at the same
        // content, so a re-dispatch writes nothing and still completes.
        CanonicalScheduleRecord record = CalendarTestData.Record(
            stableIdentity: "lesson-1",
            contentHash: "sha256:content");
        Guid user1 = Guid.CreateVersion7();

        Harness harness = new();
        harness.Records.Add(record);
        harness.Cohort.Add(Target(user1, "t1"));
        harness.Holders.Add(Holder(user1, "lesson-1", "sha256:content"));
        harness.Diffs.Add(Diff(Created(record.Id)));

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        Assert.Equal(IncrementalDispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal(0, result.Inserted);
        Assert.Empty(harness.Client.Inserts);
        Assert.True(harness.Diffs.WasDispatched);
    }

    [Fact]
    public async Task AnUpdatedLessonPatchesHoldersWhoseContentMovedAndWidensToNewMatches()
    {
        CanonicalScheduleRecord updated = CalendarTestData.Record(
            stableIdentity: "lesson-1",
            contentHash: "sha256:new");
        Guid changed = Guid.CreateVersion7();
        Guid unchanged = Guid.CreateVersion7();
        Guid newcomer = Guid.CreateVersion7();

        Harness harness = new();
        harness.Records.Add(updated);
        harness.Holders.Add(Holder(changed, "lesson-1", "sha256:old"));
        harness.Holders.Add(Holder(unchanged, "lesson-1", "sha256:new"));
        harness.Cohort.Add(Target(changed, "t1"));
        harness.Cohort.Add(Target(unchanged, "t2"));
        harness.Cohort.Add(Target(newcomer, "t3"));
        harness.Diffs.Add(Diff(Updated(Guid.CreateVersion7(), updated.Id)));

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        Assert.Equal(IncrementalDispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal(1, result.Patched);
        Assert.Equal(1, result.Inserted);
        Assert.Single(harness.Client.Patches);
        Assert.Single(harness.Client.Inserts);
        Assert.Contains(harness.Mappings.Updated, update => update.UserId == changed);
    }

    [Fact]
    public async Task AnUpdatedLessonThatNoLongerAppliesRemovesItFromAHolder()
    {
        // The lesson narrowed to a group the holder is not in, so it is deleted from their calendar.
        CanonicalScheduleRecord narrowed = CalendarTestData.Record(
            stableIdentity: "lesson-1",
            scope: AudienceScope.SelectedGroups,
            selectors: [("group", "B")]);
        Guid holder = Guid.CreateVersion7();

        Harness harness = new();
        harness.Records.Add(narrowed);
        harness.Holders.Add(Holder(holder, "lesson-1", "sha256:old"));
        harness.Cohort.Add(Target(
            holder,
            "t1",
            CalendarTestData.Profile(selectors:
                new Dictionary<string, string>(StringComparer.Ordinal) { ["group"] = "A" })));
        harness.Diffs.Add(Diff(Updated(Guid.CreateVersion7(), narrowed.Id)));

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        Assert.Equal(1, result.Deleted);
        Assert.Single(harness.Client.Deletes);
        Assert.Contains(harness.Mappings.Removed, removed => removed.UserId == holder);
    }

    [Fact]
    public async Task ADeletedLessonIsRemovedFromEveryHolder()
    {
        CanonicalScheduleRecord removedRecord = CalendarTestData.Record(stableIdentity: "lesson-1");
        Guid user1 = Guid.CreateVersion7();
        Guid user2 = Guid.CreateVersion7();

        Harness harness = new();
        harness.Records.Add(removedRecord);
        harness.Holders.Add(Holder(user1, "lesson-1", "sha256:old"));
        harness.Holders.Add(Holder(user2, "lesson-1", "sha256:old"));
        harness.ByUserId.Add(Target(user1, "t1"));
        harness.ByUserId.Add(Target(user2, "t2"));
        harness.Diffs.Add(Diff(Deleted(removedRecord.Id)));

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        Assert.Equal(IncrementalDispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(2, harness.Client.Deletes.Count);
        Assert.Equal(2, harness.Mappings.Removed.Count);
    }

    [Fact]
    public async Task AFrozenSystemDispatchesNothing()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record();
        Harness harness = new() { Frozen = true };
        harness.Records.Add(record);
        harness.Cohort.Add(Target(Guid.CreateVersion7(), "t1"));
        harness.Diffs.Add(Diff(Created(record.Id)));

        IncrementalCalendarSyncRunResult run = await harness.Build().RunPendingAsync(CancellationToken.None);

        Assert.True(run.Frozen);
        Assert.Empty(run.Diffs);
        Assert.Empty(harness.Client.Inserts);
        Assert.False(harness.Diffs.WasDispatched);
    }

    [Fact]
    public async Task ATransientFailureDefersTheDiffWithoutDispatchingIt()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-1");
        Harness harness = new();
        harness.Records.Add(record);
        harness.Cohort.Add(Target(Guid.CreateVersion7(), "t1"));
        harness.Client.TransientOnInsert = true;
        harness.Diffs.Add(Diff(Created(record.Id)));

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        Assert.Equal(IncrementalDispatchOutcome.Deferred, result.Outcome);
        Assert.False(harness.Diffs.WasDispatched);
        Assert.Equal(1, harness.Diffs.FailureRecorded);
    }

    [Fact]
    public async Task TransientFailuresBeyondTheLimitGiveUp()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-1");
        Harness harness = new() { Options = new IncrementalSyncOptions { MaxDispatchAttempts = 1 } };
        harness.Records.Add(record);
        harness.Cohort.Add(Target(Guid.CreateVersion7(), "t1"));
        harness.Client.TransientOnInsert = true;
        harness.Diffs.Add(Diff(Created(record.Id)));

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        Assert.Equal(IncrementalDispatchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ADeadCredentialIsFlaggedAndDoesNotBlockOtherUsersOrTheDiff()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-1");
        Guid dead = Guid.CreateVersion7();
        Guid healthy = Guid.CreateVersion7();

        Harness harness = new();
        harness.Records.Add(record);
        harness.Cohort.Add(Target(dead, "dead-token"));
        harness.Cohort.Add(Target(healthy, "good-token"));
        harness.Client.DeadTokens.Add("dead-token");
        harness.Diffs.Add(Diff(Created(record.Id)));

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        // The healthy user still gets their event, the dead one is flagged, and the diff completes.
        Assert.Equal(IncrementalDispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.ReauthFlagged);
        Assert.Contains(dead, harness.Connections.FlaggedForReauth);
        Assert.True(harness.Diffs.WasDispatched);
    }

    [Fact]
    public async Task ADiffThatIsNoLongerPendingIsSkipped()
    {
        // The scan named a diff, but a concurrent pass (or a state change) means the load returns
        // nothing: the stage reports it and moves on rather than writing anything.
        Harness harness = new();
        harness.Diffs.Add(Diff(Created(Guid.CreateVersion7())));
        harness.Diffs.ReturnNullOnLoad = true;

        IncrementalCalendarSyncDiffResult result = await harness.RunSingleAsync();

        Assert.Equal(IncrementalDispatchOutcome.NoLongerPending, result.Outcome);
        Assert.Empty(harness.Client.Inserts);
    }

    private static CalendarSyncTarget Target(Guid userId, string token, StudentProfileView? profile = null) =>
        new()
        {
            UserId = userId,
            ProtectedRefreshToken = $"protected:{token}",
            ManagedCalendarId = $"cal-{userId:N}",
            Profile = (profile ?? CalendarTestData.Profile()) with { UserId = userId },
        };

    private static CalendarEventMappingView Holder(Guid userId, string stableIdentity, string contentHash) =>
        new()
        {
            UserId = userId,
            StableIdentity = stableIdentity,
            GoogleCalendarId = $"cal-{userId:N}",
            GoogleEventId = $"evt-{userId:N}",
            ContentHash = contentHash,
            CanonicalRecordId = Guid.CreateVersion7(),
        };

    private static ScheduleDiffEntry Created(Guid currentId) => new()
    {
        Change = ScheduleDiffChange.Created,
        Match = ScheduleDiffMatch.None,
        CurrentRecordId = currentId,
    };

    private static ScheduleDiffEntry Updated(Guid previousId, Guid currentId) => new()
    {
        Change = ScheduleDiffChange.Updated,
        Match = ScheduleDiffMatch.ExactStableIdentity,
        PreviousRecordId = previousId,
        CurrentRecordId = currentId,
    };

    private static ScheduleDiffEntry Deleted(Guid previousId) => new()
    {
        Change = ScheduleDiffChange.Deleted,
        Match = ScheduleDiffMatch.None,
        PreviousRecordId = previousId,
    };

    private static DispatchableDiff Diff(params ScheduleDiffEntry[] entries) => new()
    {
        DiffId = Guid.CreateVersion7(),
        SourceId = Source,
        Entries = entries,
    };

    private sealed class Harness
    {
        public bool Frozen { get; init; }

        public IncrementalSyncOptions Options { get; init; } = new();

        public List<CanonicalScheduleRecord> Records { get; } = [];

        public List<CalendarSyncTarget> Cohort { get; } = [];

        public List<CalendarSyncTarget> ByUserId { get; } = [];

        public List<CalendarEventMappingView> Holders { get; } = [];

        public FakeDiffStore Diffs { get; } = new();

        public FakeCalendarClient Client { get; } = new();

        public FakeConnectionStore Connections { get; } = new();

        public FakeMappingStore Mappings { get; } = new();

        public IncrementalCalendarSyncService Build() => new(
            Diffs,
            new FakeScheduleReadStore(Records),
            new FakeTargetStore(Cohort, ByUserId.Count == 0 ? Cohort : ByUserId),
            Connections,
            Preload(Mappings),
            Client,
            new FakeTokenProtector(),
            new FakeFreezeStore(Frozen),
            Options,
            new FixedTimeProvider(Now));

        public async Task<IncrementalCalendarSyncDiffResult> RunSingleAsync()
        {
            IncrementalCalendarSyncRunResult run = await Build().RunPendingAsync(CancellationToken.None);
            return Assert.Single(run.Diffs);
        }

        private FakeMappingStore Preload(FakeMappingStore mappings)
        {
            mappings.Seed(Holders);
            return mappings;
        }
    }

    private sealed class FakeDiffStore : IScheduleDiffStore
    {
        private readonly List<DispatchableDiff> diffs = [];

        public bool ReturnNullOnLoad { get; set; }

        public bool WasDispatched { get; private set; }

        public int FailureRecorded { get; private set; }

        public void Add(DispatchableDiff diff) => diffs.Add(diff);

        public Task<IReadOnlyList<Guid>> ListPendingDispatchAsync(
            int limit,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([.. diffs.Take(limit).Select(diff => diff.DiffId)]);

        public Task<DispatchableDiff?> LoadForDispatchAsync(
            Guid diffId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReturnNullOnLoad
                ? null
                : diffs.SingleOrDefault(diff => diff.DiffId == diffId));

        public Task MarkDispatchedAsync(
            Guid diffId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            WasDispatched = true;
            return Task.CompletedTask;
        }

        public Task<CalendarDispatchState> RecordDispatchFailureAsync(
            Guid diffId,
            string reason,
            TimeSpan baseRetryDelay,
            int maxAttempts,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            FailureRecorded++;
            return Task.FromResult(FailureRecorded >= maxAttempts
                ? CalendarDispatchState.Failed
                : CalendarDispatchState.Pending);
        }

        public Task<ScheduleDiffInput?> LoadAsync(Guid revisionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListPendingDiffAsync(int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScheduleDiffPersistenceResult> SaveAsync(
            ScheduleDiff diff,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeCalendarClient : IUserCalendarClient
    {
        public List<(string CalendarId, ManagedCalendarEvent Event)> Inserts { get; } = [];

        public List<(string CalendarId, ManagedCalendarEvent Event)> Patches { get; } = [];

        public List<(string CalendarId, string EventId)> Deletes { get; } = [];

        public HashSet<string> DeadTokens { get; } = new(StringComparer.Ordinal);

        public bool TransientOnInsert { get; set; }

        public Task<CalendarEventInsertOutcome> InsertEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            GuardCredential(access);
            if (TransientOnInsert)
            {
                throw new GoogleCalendarTransientException("rate limited");
            }

            Inserts.Add((calendarId, calendarEvent));
            return Task.FromResult(CalendarEventInsertOutcome.Inserted);
        }

        public Task<CalendarEventPatchOutcome> PatchEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            GuardCredential(access);
            Patches.Add((calendarId, calendarEvent));
            return Task.FromResult(CalendarEventPatchOutcome.Patched);
        }

        public Task<CalendarEventDeleteOutcome> DeleteEventAsync(
            CalendarAccess access,
            string calendarId,
            string eventId,
            CancellationToken cancellationToken)
        {
            GuardCredential(access);
            Deletes.Add((calendarId, eventId));
            return Task.FromResult(CalendarEventDeleteOutcome.Deleted);
        }

        public Task<string> CreateManagedCalendarAsync(
            CalendarAccess access,
            string calendarSummary,
            string timeZoneId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        private void GuardCredential(CalendarAccess access)
        {
            if (DeadTokens.Contains(access.RefreshToken))
            {
                throw new GoogleCalendarCredentialException("credential rejected");
            }
        }
    }

    private sealed class FakeConnectionStore : IGoogleCalendarConnectionStore
    {
        public List<Guid> FlaggedForReauth { get; } = [];

        public Task MarkNeedsReauthorizationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            FlaggedForReauth.Add(userId);
            return Task.CompletedTask;
        }

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
    }

    private sealed class FakeMappingStore : IUserCalendarEventMappingStore
    {
        private readonly List<CalendarEventMappingView> holders = [];

        public List<UserCalendarEventMapping> Added { get; } = [];

        public List<(Guid UserId, string StableIdentity, string ContentHash)> Updated { get; } = [];

        public List<(Guid UserId, string StableIdentity)> Removed { get; } = [];

        public void Seed(IEnumerable<CalendarEventMappingView> views) => holders.AddRange(views);

        public Task<IReadOnlyList<CalendarEventMappingView>> ListForStableIdentityAsync(
            SourceId sourceId,
            string stableIdentity,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEventMappingView>>(
                [.. holders.Where(holder =>
                    string.Equals(holder.StableIdentity, stableIdentity, StringComparison.Ordinal))]);

        public Task<CalendarEventMappingAddOutcome> AddAsync(
            UserCalendarEventMapping mapping,
            CancellationToken cancellationToken)
        {
            Added.Add(mapping);
            holders.Add(new CalendarEventMappingView
            {
                UserId = mapping.UserId,
                StableIdentity = mapping.StableIdentity,
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
            Updated.Add((userId, stableIdentity, contentHash));
            return Task.CompletedTask;
        }

        public Task<CalendarEventMappingRemoveOutcome> RemoveAsync(
            Guid userId,
            string stableIdentity,
            CancellationToken cancellationToken)
        {
            Removed.Add((userId, stableIdentity));
            holders.RemoveAll(holder => holder.UserId == userId
                && string.Equals(holder.StableIdentity, stableIdentity, StringComparison.Ordinal));
            return Task.FromResult(CalendarEventMappingRemoveOutcome.Removed);
        }

        public Task<IReadOnlySet<string>> ListStableIdentitiesForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTargetStore(
        IReadOnlyList<CalendarSyncTarget> cohort,
        IReadOnlyList<CalendarSyncTarget> byUserId) : ICalendarSyncTargetReadStore
    {
        public Task<IReadOnlyList<CalendarSyncTarget>> ListCohortTargetsAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarSyncTarget>>([.. cohort.Where(target =>
                target.Profile.ClassYear == classYear
                && target.Profile.ProgramLanguage == programLanguage
                && string.Equals(target.Profile.AcademicYear, academicYear, StringComparison.Ordinal))]);

        public Task<IReadOnlyList<CalendarSyncTarget>> ListTargetsByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarSyncTarget>>(
                [.. byUserId.Where(target => userIds.Contains(target.UserId))]);
    }

    private sealed class FakeScheduleReadStore(IReadOnlyList<CanonicalScheduleRecord> records)
        : ICanonicalScheduleReadStore
    {
        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListRecordsByIdsAsync(
            IReadOnlyCollection<Guid> recordIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CanonicalScheduleRecord>>(
                [.. records.Where(record => recordIds.Contains(record.Id))]);

        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListCurrentPublishedRecordsAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
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
