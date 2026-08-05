using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers converging a student's calendar onto a changed academic profile (ADR-096), including the
/// boundary that keeps its deletions from becoming deletion-by-absence.
/// </summary>
public sealed class ProfileChangeResyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequestedAt = Now.AddMinutes(-5);

    private static readonly Guid UserId = Guid.CreateVersion7();

    private static readonly SourceId Source = SourceId.Parse("G1-TR-ANNUAL");

    [Fact]
    public async Task ItWritesWhatNowAppliesAndRemovesWhatNoLongerDoes()
    {
        // The student moved from practice group A to B. The A lesson is still published — it is
        // simply somebody else's now — and the B lesson has been published all along.
        CanonicalScheduleRecord groupA = Record("lesson-a", ("practiceGroup", "A"));
        CanonicalScheduleRecord groupB = Record("lesson-b", ("practiceGroup", "B"));

        FakeCalendarClient client = new();
        FakeMappingStore mappings = new(Mapping("lesson-a", "evt-a"));
        FakeConnectionStore connections = new(Pending());

        ProfileResyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore([groupB], [groupA, groupB]),
            mappings,
            client,
            Profile("B")).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.EventsRemoved);
        Assert.Equal(1, result.EventsWritten);

        Assert.Equal("evt-a", Assert.Single(client.Deletes).EventId);
        Assert.Equal("lesson-a", Assert.Single(mappings.Removed));
        Assert.Equal(
            "lesson-b",
            Assert.Single(client.Inserts).Event.PrivateProperties["stableIdentity"]);
        Assert.Equal(RequestedAt, connections.CompletedWithToken);
    }

    [Fact]
    public async Task AMappingWhoseLessonIsNoLongerPublishedIsLeftAlone()
    {
        // The lesson is absent from published truth, so removing it here would be deleting from
        // absence. Retiring it belongs to the semantic diff (AI_GUIDELINE §13).
        FakeCalendarClient client = new();
        FakeMappingStore mappings = new(Mapping("retired-lesson", "evt-retired"));

        ProfileResyncResult result = Single(await Build(
            new FakeConnectionStore(Pending()),
            new FakeScheduleReadStore([], []),
            mappings,
            client,
            Profile("B")).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.Completed, result.Outcome);
        Assert.Equal(0, result.EventsRemoved);
        Assert.Empty(client.Deletes);
        Assert.Empty(mappings.Removed);
    }

    [Fact]
    public async Task AMappingWhoseLessonBelongsToAnotherSourceIsNotTreatedAsLive()
    {
        // The identity matches, the source does not. Comparing both is what stops one source's
        // identity from authorizing a deletion in another's.
        FakeCalendarClient client = new();
        FakeMappingStore mappings = new(Mapping("shared-identity", "evt-x"));
        CanonicalScheduleRecord elsewhere = Record(
            "shared-identity",
            ("practiceGroup", "A"),
            sourceId: SourceId.Parse("G1-TR-PRACTICE"));

        ProfileResyncResult result = Single(await Build(
            new FakeConnectionStore(Pending()),
            new FakeScheduleReadStore([], [elsewhere]),
            mappings,
            client,
            Profile("B")).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.Completed, result.Outcome);
        Assert.Empty(client.Deletes);
    }

    [Fact]
    public async Task AnEventThatStillAppliesIsNeitherRewrittenNorRemoved()
    {
        CanonicalScheduleRecord kept = Record("lesson-b", ("practiceGroup", "B"));
        FakeCalendarClient client = new();
        FakeMappingStore mappings = new(Mapping("lesson-b", "evt-b"));

        ProfileResyncResult result = Single(await Build(
            new FakeConnectionStore(Pending()),
            new FakeScheduleReadStore([kept], [kept]),
            mappings,
            client,
            Profile("B")).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.Completed, result.Outcome);
        Assert.Empty(client.Inserts);
        Assert.Empty(client.Deletes);
    }

    [Fact]
    public async Task ThePerCycleBudgetDefersTheRemainderAndKeepsTheRequest()
    {
        CanonicalScheduleRecord first = Record("lesson-b1", ("practiceGroup", "B"));
        CanonicalScheduleRecord second = Record("lesson-b2", ("practiceGroup", "B"));
        CanonicalScheduleRecord third = Record("lesson-b3", ("practiceGroup", "B"));

        FakeCalendarClient client = new();
        FakeConnectionStore connections = new(Pending());

        ProfileResyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore([first, second, third], [first, second, third]),
            new FakeMappingStore(),
            client,
            Profile("B"),
            options: new ProfileResyncOptions { CalendarOperationsPerConnectionPerCycle = 2 })
            .RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.InProgress, result.Outcome);
        Assert.Equal(2, client.Inserts.Count);

        // A quota yield is not a failure and must not clear the request.
        Assert.Null(connections.CompletedWithToken);
    }

    [Fact]
    public async Task RemovalsRunBeforeAdditionsWhenTheBudgetIsTight()
    {
        CanonicalScheduleRecord stale = Record("lesson-a", ("practiceGroup", "A"));
        CanonicalScheduleRecord fresh = Record("lesson-b", ("practiceGroup", "B"));

        FakeCalendarClient client = new();
        FakeMappingStore mappings = new(Mapping("lesson-a", "evt-a"));

        await Build(
            new FakeConnectionStore(Pending()),
            new FakeScheduleReadStore([fresh], [stale, fresh]),
            mappings,
            client,
            Profile("B"),
            options: new ProfileResyncOptions { CalendarOperationsPerConnectionPerCycle = 1 })
            .RunPendingAsync(CancellationToken.None);

        Assert.Single(client.Deletes);
        Assert.Empty(client.Inserts);
    }

    [Fact]
    public async Task AProfileChangedAgainDuringThePassKeepsItsNewerRequest()
    {
        FakeConnectionStore connections = new(Pending())
        {
            CompletionOutcome = CompleteProfileResyncOutcome.Superseded,
        };

        ProfileResyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore([], []),
            new FakeMappingStore(),
            new FakeCalendarClient(),
            Profile("B")).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.Superseded, result.Outcome);
    }

    [Fact]
    public async Task TheGlobalFreezeStopsEveryResync()
    {
        FakeCalendarClient client = new();

        ProfileResyncRunResult run = await Build(
            new FakeConnectionStore(Pending()),
            new FakeScheduleReadStore([], []),
            new FakeMappingStore(),
            client,
            Profile("B"),
            frozen: true).RunPendingAsync(CancellationToken.None);

        Assert.True(run.Frozen);
        Assert.Empty(run.Users);
        Assert.Empty(client.Deletes);
    }

    [Fact]
    public async Task AScopedFreezeLeavesTheRequestPending()
    {
        FakeConnectionStore connections = new(Pending());
        CanonicalScheduleRecord stale = Record("lesson-a", ("practiceGroup", "A"));
        FakeCalendarClient client = new();

        ProfileResyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore([], [stale]),
            new FakeMappingStore(Mapping("lesson-a", "evt-a")),
            client,
            Profile("B"),
            scopedFreeze: true).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.Frozen, result.Outcome);
        Assert.Empty(client.Deletes);
        Assert.Null(connections.CompletedWithToken);
    }

    [Fact]
    public async Task ADeadCredentialFlagsTheConnectionAndKeepsTheRequest()
    {
        CanonicalScheduleRecord fresh = Record("lesson-b", ("practiceGroup", "B"));
        FakeConnectionStore connections = new(Pending());

        ProfileResyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore([fresh], [fresh]),
            new FakeMappingStore(),
            new FakeCalendarClient
            {
                ThrowOnInsert = new GoogleCalendarCredentialException("The grant was revoked."),
            },
            Profile("B")).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.AuthorizationRequired, result.Outcome);
        Assert.True(connections.FlaggedForReauthorization);
        Assert.Null(connections.CompletedWithToken);
    }

    [Fact]
    public async Task AMissingCalendarBecomesAnExplicitRepairState()
    {
        CanonicalScheduleRecord fresh = Record("lesson-b", ("practiceGroup", "B"));
        FakeConnectionStore connections = new(Pending());

        ProfileResyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore([fresh], [fresh]),
            new FakeMappingStore(),
            new FakeCalendarClient
            {
                ThrowOnInsert =
                    new GoogleManagedCalendarUnavailableException("The calendar is gone."),
            },
            Profile("B")).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.CalendarRepairRequired, result.Outcome);
        Assert.True(connections.FlaggedCalendarUnavailable);
        Assert.Null(connections.CompletedWithToken);
    }

    [Fact]
    public async Task AMissingProfileResolvesNothingAndKeepsTheRequest()
    {
        FakeConnectionStore connections = new(Pending());
        FakeCalendarClient client = new();

        ProfileResyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore([], []),
            new FakeMappingStore(Mapping("lesson-a", "evt-a")),
            client,
            profile: null).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.ProfileMissing, result.Outcome);
        Assert.Empty(client.Deletes);
        Assert.Null(connections.CompletedWithToken);
    }

    [Fact]
    public async Task ACalendarFailureIsIsolatedAndTheRequestSurvives()
    {
        CanonicalScheduleRecord fresh = Record("lesson-b", ("practiceGroup", "B"));
        FakeConnectionStore connections = new(Pending());

        ProfileResyncResult result = Single(await Build(
            connections,
            new FakeScheduleReadStore([fresh], [fresh]),
            new FakeMappingStore(),
            new FakeCalendarClient
            {
                ThrowOnInsert = new GoogleCalendarSyncException("Google said no."),
            },
            Profile("B")).RunPendingAsync(CancellationToken.None));

        Assert.Equal(ProfileResyncOutcome.Failed, result.Outcome);
        Assert.Contains("Google said no.", result.FailureReason);
        Assert.Null(connections.CompletedWithToken);
    }

    [Fact]
    public async Task TheStoredCredentialIsDecryptedBeforeItReachesTheClient()
    {
        CanonicalScheduleRecord fresh = Record("lesson-b", ("practiceGroup", "B"));
        FakeCalendarClient client = new();

        await Build(
            new FakeConnectionStore(Pending()),
            new FakeScheduleReadStore([fresh], [fresh]),
            new FakeMappingStore(),
            client,
            Profile("B")).RunPendingAsync(CancellationToken.None);

        Assert.All(client.RefreshTokensSeen, token => Assert.Equal("refresh-token", token));
    }

    private static ProfileChangeResyncService Build(
        FakeConnectionStore connections,
        FakeScheduleReadStore schedule,
        FakeMappingStore mappings,
        FakeCalendarClient client,
        StudentProfileView? profile,
        bool frozen = false,
        bool scopedFreeze = false,
        ProfileResyncOptions? options = null) =>
        new(
            connections,
            new FakeProfileStore(profile),
            schedule,
            mappings,
            client,
            new FakeTokenProtector(),
            new FakeFreezeStore(frozen, scopedFreeze),
            options ?? new ProfileResyncOptions(),
            new FixedTimeProvider(Now),
            TestDepartmentColors.Create());

    private static StudentProfileView Profile(string practiceGroup) =>
        CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = practiceGroup,
            }) with
        {
            UserId = UserId,
        };

    private static CanonicalScheduleRecord Record(
        string stableIdentity,
        (string Dimension, string Value) selector,
        SourceId? sourceId = null) =>
        CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [selector],
            stableIdentity: stableIdentity,
            sourceId: sourceId ?? Source);

    private static CalendarEventMappingView Mapping(string stableIdentity, string eventId) => new()
    {
        UserId = UserId,
        StableIdentity = stableIdentity,
        SourceId = Source,
        GoogleCalendarId = "cal",
        GoogleEventId = eventId,
        ContentHash = "sha256:content",
        CanonicalRecordId = Guid.CreateVersion7(),
    };

    private static PendingProfileResync Pending() => new()
    {
        UserId = UserId,
        ProtectedRefreshToken = "protected:refresh-token",
        ManagedCalendarId = "cal",
        RequiredSinceUtc = RequestedAt,
    };

    private static ProfileResyncResult Single(ProfileResyncRunResult run) => Assert.Single(run.Users);

    private sealed class FakeConnectionStore(params PendingProfileResync[] pending)
        : IGoogleCalendarConnectionStore
    {
        public CompleteProfileResyncOutcome CompletionOutcome { get; init; } =
            CompleteProfileResyncOutcome.Completed;

        /// <summary>The workflow token the service presented, or null when it never completed.</summary>
        public DateTimeOffset? CompletedWithToken { get; private set; }

        public bool FlaggedForReauthorization { get; private set; }

        public bool FlaggedCalendarUnavailable { get; private set; }

        public Task<IReadOnlyList<PendingProfileResync>> ListPendingProfileResyncAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PendingProfileResync>>([.. pending.Take(limit)]);

        public Task<CompleteProfileResyncOutcome> CompleteProfileResyncAsync(
            Guid userId,
            DateTimeOffset expectedRequiredSinceUtc,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            CompletedWithToken = expectedRequiredSinceUtc;
            return Task.FromResult(CompletionOutcome);
        }

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

        public Task<RequestReconciliationOutcome> RequestReconciliationAsync(
            Guid userId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeScheduleReadStore(
        IReadOnlyList<CanonicalScheduleRecord> applicableProgramRecords,
        IReadOnlyList<CanonicalScheduleRecord> liveAcrossPrograms) : ICanonicalScheduleReadStore
    {
        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListCurrentPublishedRecordsAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult(applicableProgramRecords);

        public Task<IReadOnlyList<PublishedRecordIdentity>> ListCurrentPublishedIdentitiesAsync(
            string academicYear,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PublishedRecordIdentity>>(
            [
                .. liveAcrossPrograms.Select(record => new PublishedRecordIdentity
                {
                    SourceId = record.SourceId,
                    StableIdentity = record.StableIdentity,
                }),
            ]);

        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListRecordsByIdsAsync(
            IReadOnlyCollection<Guid> recordIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeMappingStore(params CalendarEventMappingView[] held)
        : IUserCalendarEventMappingStore
    {
        private readonly List<CalendarEventMappingView> rows = [.. held];

        public List<UserCalendarEventMapping> Added { get; } = [];

        public List<string> Removed { get; } = [];

        public Task<IReadOnlyList<CalendarEventMappingView>> ListForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEventMappingView>>([.. rows]);

        public Task<CalendarEventMappingAddOutcome> AddAsync(
            UserCalendarEventMapping mapping,
            CancellationToken cancellationToken)
        {
            Added.Add(mapping);
            return Task.FromResult(CalendarEventMappingAddOutcome.Added);
        }

        public Task<CalendarEventMappingRemoveOutcome> RemoveAsync(
            Guid userId,
            string stableIdentity,
            CancellationToken cancellationToken)
        {
            Removed.Add(stableIdentity);
            int removed = rows.RemoveAll(row =>
                string.Equals(row.StableIdentity, stableIdentity, StringComparison.Ordinal));
            return Task.FromResult(removed > 0
                ? CalendarEventMappingRemoveOutcome.Removed
                : CalendarEventMappingRemoveOutcome.NotFound);
        }

        public Task<IReadOnlySet<string>> ListStableIdentitiesForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CalendarSyncProgressView> GetProgressForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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
    }

    private sealed class FakeCalendarClient : IUserCalendarClient
    {
        public List<(string CalendarId, ManagedCalendarEvent Event)> Inserts { get; } = [];

        public List<(string CalendarId, string EventId)> Deletes { get; } = [];

        public List<string> RefreshTokensSeen { get; } = [];

        public Exception? ThrowOnInsert { get; init; }

        public Exception? ThrowOnDelete { get; init; }

        public Task<CalendarEventInsertOutcome> InsertEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken)
        {
            RefreshTokensSeen.Add(access.RefreshToken);
            if (ThrowOnInsert is not null)
            {
                throw ThrowOnInsert;
            }

            Inserts.Add((calendarId, calendarEvent));
            return Task.FromResult(CalendarEventInsertOutcome.Inserted);
        }

        public Task<CalendarEventDeleteOutcome> DeleteEventAsync(
            CalendarAccess access,
            string calendarId,
            string eventId,
            CancellationToken cancellationToken)
        {
            RefreshTokensSeen.Add(access.RefreshToken);
            if (ThrowOnDelete is not null)
            {
                throw ThrowOnDelete;
            }

            Deletes.Add((calendarId, eventId));
            return Task.FromResult(CalendarEventDeleteOutcome.Deleted);
        }

        public Task EnsureEventLabelAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEventLabel label,
            CancellationToken cancellationToken) => Task.CompletedTask;

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

        public Task<CalendarEventPatchOutcome> PatchEventAsync(
            CalendarAccess access,
            string calendarId,
            ManagedCalendarEvent calendarEvent,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeProfileStore(StudentProfileView? profile) : IStudentProfileStore
    {
        public Task<StudentProfileView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(profile is not null);

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

    /// <remarks>
    /// <c>IsFrozenAsync</c> is deliberately not overridden: its default composes the global switch
    /// with the scoped one, which is the behaviour under test.
    /// </remarks>
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
