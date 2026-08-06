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
/// The re-authorization catch-up stage, exercised against fakes up to the Google boundary
/// (ADR-060). Every delete in these tests originates in a replayed Deleted/Updated diff entry.
/// </summary>
public sealed class CalendarReconciliationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequiredSince =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ItReplaysSecondaryUpdateAndDeleteInOrderThenCompletesOnAnEmptyScan()
    {
        Guid userId = Guid.CreateVersion7();
        CanonicalScheduleRecord original = CalendarTestData.Record(
            stableIdentity: "lesson-at-0900",
            contentHash: "sha256:original");
        CanonicalScheduleRecord moved = CalendarTestData.Record(
            stableIdentity: "lesson-at-1000",
            contentHash: "sha256:moved");
        CanonicalScheduleRecord corrected = CalendarTestData.Record(
            stableIdentity: "lesson-at-1000",
            contentHash: "sha256:corrected");

        Harness harness = new(userId);
        harness.Records.AddRange([original, moved, corrected]);
        harness.Mappings.Seed(Mapping(userId, original, "google-event-1"));
        harness.Diffs.Add(Diff(
            RequiredSince.AddMinutes(1),
            Updated(original.Id, moved.Id, ScheduleDiffMatch.SecondaryAttributes)));
        harness.Diffs.Add(Diff(
            RequiredSince.AddMinutes(2),
            Updated(moved.Id, corrected.Id, ScheduleDiffMatch.ExactStableIdentity)));
        harness.Diffs.Add(Diff(
            RequiredSince.AddMinutes(3),
            Deleted(corrected.Id)));

        CalendarReconciliationUserResult first = await harness.RunSingleAsync();

        Assert.Equal(CalendarReconciliationOutcome.InProgress, first.Outcome);
        Assert.Equal(3, first.DiffsReplayed);
        Assert.Equal(2, first.Patched);
        Assert.Equal(1, first.Deleted);
        Assert.Equal(
            harness.Diffs.Items.Select(diff => diff.DiffId),
            harness.Connections.Advanced.Select(advance => advance.DiffId));
        Assert.All(
            harness.Client.Patches,
            patch => Assert.Equal("google-event-1", patch.Event.EventId));
        Assert.Equal("google-event-1", Assert.Single(harness.Client.Deletes).EventId);
        Assert.Empty(harness.Mappings.Items);

        CalendarReconciliationUserResult second = await harness.RunSingleAsync();

        Assert.Equal(CalendarReconciliationOutcome.Completed, second.Outcome);
        Assert.True(harness.Connections.Completed);
    }

    [Fact]
    public async Task AnEmptyReplayNeverDeletesAStaleMappingFromCurrentStateAbsence()
    {
        Guid userId = Guid.CreateVersion7();
        CanonicalScheduleRecord stale = CalendarTestData.Record(stableIdentity: "stale");
        Harness harness = new(userId);
        harness.Mappings.Seed(Mapping(userId, stale, "google-event-stale"));

        CalendarReconciliationUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarReconciliationOutcome.Completed, result.Outcome);
        Assert.Empty(harness.Client.Deletes);
        Assert.Single(harness.Mappings.Items);
    }

    [Fact]
    public async Task ItInsertsACreatedApplicableLessonAndAdvancesOnlyAfterTheDiff()
    {
        Guid userId = Guid.CreateVersion7();
        CanonicalScheduleRecord created = CalendarTestData.Record(stableIdentity: "new-lesson");
        Harness harness = new(userId);
        harness.Records.Add(created);
        DispatchedDiff diff = Diff(RequiredSince.AddMinutes(1), Created(created.Id));
        harness.Diffs.Add(diff);

        CalendarReconciliationUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarReconciliationOutcome.InProgress, result.Outcome);
        Assert.Equal(1, result.Inserted);
        Assert.Single(harness.Client.Inserts);
        Assert.Equal(diff.DiffId, Assert.Single(harness.Connections.Advanced).DiffId);
        Assert.Equal("new-lesson", Assert.Single(harness.Mappings.Items).StableIdentity);
    }

    [Fact]
    public async Task ARejectedCredentialPreservesTheCursorAndRequiresAuthorizationAgain()
    {
        Guid userId = Guid.CreateVersion7();
        CanonicalScheduleRecord created = CalendarTestData.Record(stableIdentity: "new-lesson");
        Harness harness = new(userId);
        harness.Records.Add(created);
        harness.Diffs.Add(Diff(RequiredSince.AddMinutes(1), Created(created.Id)));
        harness.Client.CredentialRejected = true;

        CalendarReconciliationUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarReconciliationOutcome.AuthorizationRequired, result.Outcome);
        Assert.True(harness.Connections.FlaggedForReauthorization);
        Assert.Empty(harness.Connections.Advanced);
        Assert.Empty(harness.Mappings.Items);
    }

    [Fact]
    public async Task ATransientFailureDefersWithoutAdvancingTheCurrentDiff()
    {
        Guid userId = Guid.CreateVersion7();
        CanonicalScheduleRecord created = CalendarTestData.Record(stableIdentity: "new-lesson");
        Harness harness = new(userId);
        harness.Records.Add(created);
        harness.Diffs.Add(Diff(RequiredSince.AddMinutes(1), Created(created.Id)));
        harness.Client.TransientFailure = true;

        CalendarReconciliationUserResult result = await harness.RunSingleAsync();

        Assert.Equal(CalendarReconciliationOutcome.Deferred, result.Outcome);
        Assert.Empty(harness.Connections.Advanced);
        Assert.False(harness.Connections.Completed);
    }

    [Fact]
    public async Task AGlobalFreezeAdmitsNoReconciliationWork()
    {
        Harness harness = new(Guid.CreateVersion7()) { Frozen = true };

        CalendarReconciliationRunResult result =
            await harness.Build().RunPendingAsync(CancellationToken.None);

        Assert.True(result.Frozen);
        Assert.Empty(result.Users);
        Assert.Equal(0, harness.Connections.ListCalls);
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

    private static ScheduleDiffEntry Created(Guid currentId) => new()
    {
        Change = ScheduleDiffChange.Created,
        Match = ScheduleDiffMatch.None,
        CurrentRecordId = currentId,
    };

    private static ScheduleDiffEntry Updated(
        Guid previousId,
        Guid currentId,
        ScheduleDiffMatch match) => new()
        {
            Change = ScheduleDiffChange.Updated,
            Match = match,
            PreviousRecordId = previousId,
            CurrentRecordId = currentId,
        };

    private static ScheduleDiffEntry Deleted(Guid previousId) => new()
    {
        Change = ScheduleDiffChange.Deleted,
        Match = ScheduleDiffMatch.None,
        PreviousRecordId = previousId,
    };

    private static DispatchedDiff Diff(
        DateTimeOffset dispatchedAtUtc,
        params ScheduleDiffEntry[] entries) => new()
        {
            DiffId = Guid.CreateVersion7(),
            SourceId = SourceId.Parse("G1-TR-ANNUAL"),
            DispatchedAtUtc = dispatchedAtUtc,
            Entries = entries,
        };

    private sealed class Harness
    {
        public Harness(Guid userId)
        {
            UserId = userId;
            Connections.Pending = new PendingCalendarReconciliation
            {
                UserId = userId,
                ProtectedRefreshToken = "protected:refresh-token",
                ManagedCalendarId = $"cal-{userId:N}",
                RequiredSinceUtc = RequiredSince,
                CursorDispatchedAtUtc = RequiredSince,
                CursorDiffId = Guid.Empty,
            };
            Profile = CalendarTestData.Profile() with { UserId = userId };
        }

        public Guid UserId { get; }

        public bool Frozen { get; init; }

        public StudentProfileView Profile { get; }

        public List<CanonicalScheduleRecord> Records { get; } = [];

        public FakeDiffStore Diffs { get; } = new();

        public FakeConnectionStore Connections { get; } = new();

        public FakeMappingStore Mappings { get; } = new();

        public FakeCalendarClient Client { get; } = new();

        public CalendarReconciliationService Build() => new(
            Diffs,
            new FakeScheduleReadStore(Records),
            new FakeProfileStore(Profile),
            Connections,
            Mappings,
            Client,
            new FakeTokenProtector(),
            new FakeFreezeStore(Frozen),
            new CalendarReconciliationOptions(),
            new FixedTimeProvider(Now),
            TestDepartmentColors.Create());

        public async Task<CalendarReconciliationUserResult> RunSingleAsync()
        {
            CalendarReconciliationRunResult run =
                await Build().RunPendingAsync(CancellationToken.None);
            return Assert.Single(run.Users);
        }
    }

    private sealed class FakeDiffStore : IScheduleDiffStore
    {
        public List<DispatchedDiff> Items { get; } = [];

        public void Add(DispatchedDiff diff) => Items.Add(diff);

        public Task<IReadOnlyList<DispatchedDiff>> ListDispatchedForReplayAsync(
            DateTimeOffset afterDispatchedAtUtc,
            Guid afterDiffId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DispatchedDiff>>(
                [.. Items
                    .Where(diff => diff.DispatchedAtUtc > afterDispatchedAtUtc
                        || (diff.DispatchedAtUtc == afterDispatchedAtUtc
                            && diff.DiffId.CompareTo(afterDiffId) > 0))
                    .OrderBy(diff => diff.DispatchedAtUtc)
                    .ThenBy(diff => diff.DiffId)
                    .Take(limit)]);

        public Task<ScheduleDiffInput?> LoadAsync(
            Guid revisionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListPendingDiffAsync(
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ScheduleDiffPersistenceResult> SaveAsync(
            ScheduleDiff diff,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListPendingDispatchAsync(
            int limit,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DispatchableDiff?> LoadForDispatchAsync(
            Guid diffId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkDispatchedAsync(
            Guid diffId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarDispatchState> RecordDispatchFailureAsync(
            Guid diffId,
            string reason,
            TimeSpan baseRetryDelay,
            int maxAttempts,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeConnectionStore : ICalendarSyncConnectionStore
    {
        public PendingCalendarReconciliation? Pending { get; set; }

        public List<(DateTimeOffset DispatchedAtUtc, Guid DiffId)> Advanced { get; } = [];

        public int ListCalls { get; private set; }

        public bool Completed { get; private set; }

        public bool FlaggedForReauthorization { get; private set; }

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
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<PendingCalendarReconciliation>>(
                Pending is null ? [] : [Pending]);
        }

        public Task AdvanceReconciliationCursorAsync(
            Guid userId,
            DateTimeOffset expectedRequiredSinceUtc,
            DateTimeOffset dispatchedAtUtc,
            Guid diffId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(Pending);
            Assert.Equal(Pending.RequiredSinceUtc, expectedRequiredSinceUtc);
            Advanced.Add((dispatchedAtUtc, diffId));
            Pending = Pending with
            {
                CursorDispatchedAtUtc = dispatchedAtUtc,
                CursorDiffId = diffId,
            };
            return Task.CompletedTask;
        }

        public Task CompleteReconciliationAsync(
            Guid userId,
            DateTimeOffset expectedRequiredSinceUtc,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(Pending);
            Assert.Equal(Pending.RequiredSinceUtc, expectedRequiredSinceUtc);
            Completed = true;
            Pending = null;
            return Task.CompletedTask;
        }

        public Task MarkNeedsReauthorizationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            FlaggedForReauthorization = true;
            Pending = null;
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

    private sealed class FakeMappingStore : IUserCalendarEventMappingStore
    {
        public List<CalendarEventMappingView> Items { get; } = [];

        public void Seed(CalendarEventMappingView mapping) => Items.Add(mapping);

        public Task<IReadOnlyList<CalendarEventMappingView>> ListForStableIdentityAsync(
            SourceId sourceId,
            string stableIdentity,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEventMappingView>>(
                [.. Items.Where(mapping =>
                    string.Equals(mapping.StableIdentity, stableIdentity, StringComparison.Ordinal))]);

        public Task<CalendarEventMappingAddOutcome> AddAsync(
            UserCalendarEventMapping mapping,
            CancellationToken cancellationToken)
        {
            if (Items.Any(item => item.UserId == mapping.UserId
                && string.Equals(
                    item.StableIdentity,
                    mapping.StableIdentity,
                    StringComparison.Ordinal)))
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
            int index = Items.FindIndex(mapping => mapping.UserId == userId
                && string.Equals(
                    mapping.StableIdentity,
                    stableIdentity,
                    StringComparison.Ordinal));
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

        public Task<CalendarEventMappingReidentifyOutcome> ReidentifyAsync(
            Guid userId,
            SourceId sourceId,
            string previousStableIdentity,
            string currentStableIdentity,
            Guid canonicalRecordId,
            string contentHash,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            int previous = Items.FindIndex(mapping => mapping.UserId == userId
                && string.Equals(
                    mapping.StableIdentity,
                    previousStableIdentity,
                    StringComparison.Ordinal));
            int current = Items.FindIndex(mapping => mapping.UserId == userId
                && string.Equals(
                    mapping.StableIdentity,
                    currentStableIdentity,
                    StringComparison.Ordinal));

            if (previous < 0)
            {
                return Task.FromResult(current < 0
                    ? CalendarEventMappingReidentifyOutcome.NotFound
                    : CalendarEventMappingReidentifyOutcome.AlreadyReidentified);
            }

            if (current >= 0 && current != previous)
            {
                return Task.FromResult(CalendarEventMappingReidentifyOutcome.Conflict);
            }

            Items[previous] = Items[previous] with
            {
                StableIdentity = currentStableIdentity,
                CanonicalRecordId = canonicalRecordId,
                ContentHash = contentHash,
            };
            return Task.FromResult(CalendarEventMappingReidentifyOutcome.Reidentified);
        }

        public Task<CalendarEventMappingRemoveOutcome> RemoveAsync(
            Guid userId,
            string stableIdentity,
            CancellationToken cancellationToken)
        {
            int removed = Items.RemoveAll(mapping => mapping.UserId == userId
                && string.Equals(
                    mapping.StableIdentity,
                    stableIdentity,
                    StringComparison.Ordinal));
            return Task.FromResult(removed == 0
                ? CalendarEventMappingRemoveOutcome.NotFound
                : CalendarEventMappingRemoveOutcome.Removed);
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

        public Task<IReadOnlyList<CalendarEventMappingView>> ListForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEventMappingView>>(
                [.. Items.Where(mapping => mapping.UserId == userId)]);
    }

    private sealed class FakeCalendarClient : IUserCalendarClient
    {
        public List<(string CalendarId, ManagedCalendarEvent Event)> Inserts { get; } = [];

        public List<(string CalendarId, ManagedCalendarEvent Event)> Patches { get; } = [];

        public List<(string CalendarId, string EventId)> Deletes { get; } = [];

        public bool CredentialRejected { get; set; }

        public bool TransientFailure { get; set; }

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
            Guard();
            Inserts.Add((calendarId, calendarEvent));
            return Task.FromResult(CalendarEventInsertOutcome.Inserted);
        }

        public Task<CalendarEventPatchOutcome> PatchEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            Guard();
            Patches.Add((calendarId, calendarEvent));
            return Task.FromResult(CalendarEventPatchOutcome.Patched);
        }

        public Task<CalendarEventDeleteOutcome> DeleteEventAsync(
            CalendarAccess access,
            string calendarId,
            string eventId,
            CancellationToken cancellationToken)
        {
            Guard();
            Deletes.Add((calendarId, eventId));
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

        private void Guard()
        {
            if (CredentialRejected)
            {
                throw new GoogleCalendarCredentialException("credential rejected");
            }

            if (TransientFailure)
            {
                throw new GoogleCalendarTransientException("rate limited");
            }
        }
    }

    private sealed class FakeScheduleReadStore(IReadOnlyList<CanonicalScheduleRecord> records)
        : ICanonicalScheduleReadStore
    {
        public Task<IReadOnlyList<PublishedRecordIdentity>> ListCurrentPublishedIdentitiesAsync(
            string academicYear,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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

    private sealed class FakeProfileStore(StudentProfileView profile) : IStudentProfileStore
    {
        public Task<StudentProfileView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StudentProfileView?>(profile.UserId == userId ? profile : null);

        public Task<bool> ExistsForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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
