using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the rollover that moves stored profiles onto the year their program's sources now state
/// (ADR-115).
/// </summary>
/// <remarks>
/// The incident it was built for is the concrete case: the Grade 2 Turkish annual and practice
/// sources were repointed at the 2026-2027 workbooks while every stored Grade 2 profile still
/// said 2025-2026. Deletions are ledger-driven and fired; insertions are cohort-driven and
/// resolved to nobody, so the class watched a year of lessons disappear and nothing come back.
/// </remarks>
public sealed class ProfileAcademicYearRolloverServiceTests
{
    private const string OldYear = "2025-2026";
    private const string NewYear = "2026-2027";

    private static readonly SourceId Practice = SourceId.Parse("G2-TR-PRACTICE");

    private static readonly ProfileRolloverScope Grade2Turkish = new()
    {
        FromAcademicYear = OldYear,
        ClassYear = 2,
        ProgramLanguage = ProgramLanguage.Turkish,
    };

    [Fact]
    public async Task ThePlanCountsTheLessonsAStudentWouldStopMissingAsync()
    {
        // The whole point: under the old year these records resolve to nobody, which is exactly
        // why the cohort's calendars went empty and nothing reported a fault.
        StudentProfileView profile = Grade2Profile("A");
        List<CanonicalScheduleRecord> published =
            [.. Enumerable.Range(1, 3).Select(index => NewYearRecord($"identity-{index}"))];

        ProfileRolloverPlan plan = await Service(published, Candidate(profile))
            .PlanAsync(Grade2Turkish, CancellationToken.None);

        Assert.Equal(NewYear, plan.ToAcademicYear);
        ProfileRolloverUserPlan user = Assert.Single(plan.Users);
        Assert.Equal(3, user.GainedEventCount);
        Assert.Equal(3, plan.TotalGainedEvents);
    }

    [Fact]
    public async Task LessonsAlreadyOnTheCalendarAreNotCountedAsGainedAsync()
    {
        // A rollover re-run after a partial convergence must not re-report what is already there,
        // or the operator can never tell whether the previous one finished.
        StudentProfileView profile = Grade2Profile("A");
        CanonicalScheduleRecord held = NewYearRecord("identity-held");
        CanonicalScheduleRecord missing = NewYearRecord("identity-missing");

        ProfileRolloverPlan plan = await Service(
                [held, missing],
                Candidate(profile, held: [held]))
            .PlanAsync(Grade2Turkish, CancellationToken.None);

        Assert.Equal(1, Assert.Single(plan.Users).GainedEventCount);
    }

    [Fact]
    public async Task LastYearsEventsAreReportedAsStrandedRatherThanQueuedForDeletionAsync()
    {
        // Convergence measures its removals against the *new* year's published identities, so a
        // ledger row from the year being left is absent from that set and is left completely
        // alone (ADR-089). It stays on the calendar as history, and the operator is told so
        // rather than discovering it from a support request.
        StudentProfileView profile = Grade2Profile("A");
        CanonicalScheduleRecord current = NewYearRecord("identity-current");

        ProfileRolloverPlan plan = await Service(
                published: [current],
                Candidate(profile, extraHeldIdentities: ["identity-from-last-year"]))
            .PlanAsync(Grade2Turkish, CancellationToken.None);

        ProfileRolloverUserPlan user = Assert.Single(plan.Users);
        Assert.Equal(1, user.GainedEventCount);
        Assert.Equal(1, user.StrandedEventCount);
        Assert.Equal(1, plan.TotalStrandedEvents);
    }

    [Fact]
    public async Task AProfileWhoseSelectorsTheNewProgramRefusesIsBlockedNotRestampedAsync()
    {
        // Re-stamping this profile would store one the schema rejects, and the student would find
        // their own settings page refusing a profile they never changed.
        StudentProfileView stale = Grade2Profile("Z");

        ProfileRolloverPlan plan = await Service([NewYearRecord("identity")], Candidate(stale))
            .PlanAsync(Grade2Turkish, CancellationToken.None);

        Assert.Empty(plan.Users);
        Assert.Equal([stale.UserId], plan.BlockedByInvalidSelectors);
    }

    [Fact]
    public async Task AProfileWithNoSyncReadyConnectionIsStillMovedButQueuesNothingAsync()
    {
        // The year on a profile decides what its owner receives whenever they connect, so leaving
        // an unconnected student behind would only defer the same empty calendar.
        StudentProfileView profile = Grade2Profile("A");

        ProfileRolloverPlan plan = await Service(
                [NewYearRecord("identity")],
                Candidate(profile, syncReady: false))
            .PlanAsync(Grade2Turkish, CancellationToken.None);

        ProfileRolloverUserPlan user = Assert.Single(plan.Users);
        Assert.False(user.ConvergenceQueueable);
        Assert.Equal(1, plan.ProfilesWithoutSyncReadyConnection);
    }

    [Fact]
    public async Task ARolloverTheDeployedSchemaDoesNotStateIsRefusedAsync()
    {
        // The target year is never the caller's. If the schema has not been deployed yet there is
        // no year to move to, and stamping one anyway would split the cohort in two: existing
        // profiles on a year new sign-ups would not get.
        ProfileRolloverScope alreadyCurrent = Grade2Turkish with { FromAcademicYear = NewYear };

        ProfileRolloverRequestResult result =
            await Service([NewYearRecord("identity")], Candidate(Grade2Profile("A")))
                .RequestAsync(alreadyCurrent, "any", NoAudit, CancellationToken.None);

        Assert.Equal(ProfileRolloverOutcome.NotSupportedBySchema, result.Outcome);
        Assert.NotNull(result.Refusal);
    }

    [Fact]
    public async Task ConfirmingThePlanMovesTheProfilesAndQueuesTheConvergenceAsync()
    {
        StudentProfileView profile = Grade2Profile("A");
        RecordingRolloverStore store = new([Candidate(profile)]);
        ProfileAcademicYearRolloverService service = Service([NewYearRecord("identity")], store);

        ProfileRolloverPlan plan = await service.PlanAsync(Grade2Turkish, CancellationToken.None);
        ProfileRolloverRequestResult result = await service.RequestAsync(
            Grade2Turkish,
            plan.PlanHash,
            NoAudit,
            CancellationToken.None);

        Assert.Equal(ProfileRolloverOutcome.Moved, result.Outcome);
        Assert.Equal([profile.UserId], store.Applied);
        Assert.Equal(NewYear, store.AppliedAcademicYear);
        Assert.Equal(CurrentSupportedProfileSchema.SchemaVersion, store.AppliedSchemaVersion);
    }

    [Fact]
    public async Task AStalePlanHashIsRefusedAndNothingIsWrittenAsync()
    {
        RecordingRolloverStore store = new([Candidate(Grade2Profile("A"))]);

        ProfileRolloverRequestResult result = await Service([NewYearRecord("identity")], store)
            .RequestAsync(
                Grade2Turkish,
                "0000000000000000000000000000000000000000000000000000000000000000",
                NoAudit,
                CancellationToken.None);

        Assert.Equal(ProfileRolloverOutcome.PlanChanged, result.Outcome);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task APlanHashChangesWhenTheAffectedStudentsChangeAsync()
    {
        // Two plans of equal size over different students must not share a hash, or confirming
        // one would authorize rolling the other.
        CanonicalScheduleRecord record = NewYearRecord("identity");

        ProfileRolloverPlan first = await Service([record], Candidate(Grade2Profile("A")))
            .PlanAsync(Grade2Turkish, CancellationToken.None);
        ProfileRolloverPlan second = await Service([record], Candidate(Grade2Profile("B")))
            .PlanAsync(Grade2Turkish, CancellationToken.None);

        Assert.Equal(first.TotalGainedEvents, second.TotalGainedEvents);
        Assert.NotEqual(first.PlanHash, second.PlanHash);
    }

    [Fact]
    public async Task NothingIsWrittenWhileFrozenAsync()
    {
        // A rollover queues calendar work, so it fails closed on the same authoritative switch
        // every other calendar-touching path reads (ADR-034/043).
        RecordingRolloverStore store = new([Candidate(Grade2Profile("A"))]);

        ProfileRolloverRequestResult result =
            await Service([NewYearRecord("identity")], store, frozen: true)
                .RequestAsync(Grade2Turkish, "any", NoAudit, CancellationToken.None);

        Assert.Equal(ProfileRolloverOutcome.Frozen, result.Outcome);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task AFailedAuditAbandonsTheRolloverBeforeAnyProfileIsRewrittenAsync()
    {
        // The ordering is the guarantee: this rewrites data students entered about themselves, so
        // an unrecordable authorization must leave nothing changed (AI_GUIDELINE §19).
        RecordingRolloverStore store = new([Candidate(Grade2Profile("A"))]);
        ProfileAcademicYearRolloverService service = Service([NewYearRecord("identity")], store);
        ProfileRolloverPlan plan = await service.PlanAsync(Grade2Turkish, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestAsync(
                Grade2Turkish,
                plan.PlanHash,
                (_, _) => throw new InvalidOperationException("the audit column rejected it"),
                CancellationToken.None));

        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task TheReconcilerMovesDriftedProfilesWithoutBeingAskedAsync()
    {
        // "Kendisi değişmeli": the schema is compiled in, so deploying one that names a new year
        // is the deliberate act. A profile still on the old year is that deployment unfinished,
        // not a second decision waiting to be taken (ADR-117).
        StudentProfileView profile = Grade2Profile("A");
        RecordingRolloverStore store = new([Candidate(profile)]);

        ProfileDriftReconcileRunResult result = await Service([NewYearRecord("identity")], store)
            .ReconcileDriftAsync(NoDriftAudit, CancellationToken.None);

        ProfileDriftReconciliation moved = Assert.Single(
            result.Programs,
            program => program.Outcome is ProfileDriftOutcome.Moved);
        Assert.Equal(2, moved.ClassYear);
        Assert.Equal(NewYear, moved.ToAcademicYear);
        Assert.Equal([profile.UserId], store.Applied);
    }

    [Fact]
    public async Task AProgramInSteadyStateIsSilentAsync()
    {
        // The reconciler runs every cycle forever. If a program with nothing to do reported
        // itself, the worker log would fill with the absence of news.
        StudentProfileView current = CalendarTestData.Profile(
            classYear: 2,
            academicYear: NewYear,
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = "A",
                ["practiceSubgroup"] = "A1",
                ["anatomyGroup"] = "1",
            });
        RecordingRolloverStore store = new([Candidate(current)]);

        ProfileDriftReconcileRunResult result = await Service([NewYearRecord("identity")], store)
            .ReconcileDriftAsync(NoDriftAudit, CancellationToken.None);

        Assert.Empty(result.Programs);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task NothingIsMovedOntoAYearThatPublishesNothingYetAsync()
    {
        // Between deploying a schema that names a new year and publishing the first revision under
        // it, moving a student guarantees them an empty calendar. Waiting costs nothing.
        RecordingRolloverStore store = new([Candidate(Grade2Profile("A"))]);

        ProfileDriftReconcileRunResult result = await Service(published: [], store)
            .ReconcileDriftAsync(NoDriftAudit, CancellationToken.None);

        Assert.Contains(
            result.Programs,
            program => program.Outcome is ProfileDriftOutcome.NothingPublishedYet);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task AProfileTheTargetProgramRefusesIsReportedAndLeftAloneAsync()
    {
        StudentProfileView stale = Grade2Profile("Z");
        RecordingRolloverStore store = new([Candidate(stale)]);

        ProfileDriftReconcileRunResult result = await Service([NewYearRecord("identity")], store)
            .ReconcileDriftAsync(NoDriftAudit, CancellationToken.None);

        ProfileDriftReconciliation blocked = Assert.Single(
            result.Programs,
            program => program.Outcome is ProfileDriftOutcome.AllBlocked);
        Assert.Equal([stale.UserId], blocked.BlockedByInvalidSelectors);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task TheReconcilerMovesNothingWhileFrozenAsync()
    {
        // The freeze is the off switch: an operator who wants to time a rollover by hand freezes
        // the program and uses the screen instead.
        RecordingRolloverStore store = new([Candidate(Grade2Profile("A"))]);

        ProfileDriftReconcileRunResult result =
            await Service([NewYearRecord("identity")], store, frozen: true)
                .ReconcileDriftAsync(NoDriftAudit, CancellationToken.None);

        Assert.True(result.Frozen);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task AFailedAuditAbandonsTheAutomaticPassBeforeAnyProfileIsRewrittenAsync()
    {
        // Unattended does not mean unrecorded. A change nobody asked for is exactly the one that
        // must be reconstructable afterwards (AI_GUIDELINE §19).
        RecordingRolloverStore store = new([Candidate(Grade2Profile("A"))]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service([NewYearRecord("identity")], store).ReconcileDriftAsync(
                (_, _) => throw new InvalidOperationException("the audit column rejected it"),
                CancellationToken.None));

        Assert.Empty(store.Applied);
    }

    private static Task NoDriftAudit(
        ProfileDriftReconciliation reconciliation,
        CancellationToken cancellationToken) => Task.CompletedTask;

    private static Task NoAudit(ProfileRolloverPlan plan, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>A Grade 2 Turkish profile still stamped with the year being left.</summary>
    private static StudentProfileView Grade2Profile(string practiceGroup) =>
        CalendarTestData.Profile(
            classYear: 2,
            academicYear: OldYear,
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = practiceGroup,
                ["practiceSubgroup"] = $"{practiceGroup}1",
                ["anatomyGroup"] = "1",
            });

    /// <summary>A lesson published for the whole program under the new year.</summary>
    private static CanonicalScheduleRecord NewYearRecord(string stableIdentity) =>
        CalendarTestData.Record(
            classYear: 2,
            academicYear: NewYear,
            sourceId: Practice,
            stableIdentity: stableIdentity);

    private static ProfileRolloverCandidate Candidate(
        StudentProfileView profile,
        IReadOnlyList<CanonicalScheduleRecord>? held = null,
        IReadOnlyList<string>? extraHeldIdentities = null,
        bool syncReady = true) => new()
        {
            UserId = profile.UserId,
            Profile = profile,
            HasSyncReadyConnection = syncReady,
            Held =
            [
                .. (held ?? []).Select(record => new HeldLessonIdentity
                {
                    SourceId = Practice.Value,
                    StableIdentity = record.StableIdentity,
                }),
                .. (extraHeldIdentities ?? []).Select(identity => new HeldLessonIdentity
                {
                    SourceId = Practice.Value,
                    StableIdentity = identity,
                }),
            ],
        };

    private static ProfileAcademicYearRolloverService Service(
        IReadOnlyList<CanonicalScheduleRecord> published,
        params ProfileRolloverCandidate[] candidates) =>
        Service(published, new RecordingRolloverStore(candidates));

    private static ProfileAcademicYearRolloverService Service(
        IReadOnlyList<CanonicalScheduleRecord> published,
        RecordingRolloverStore store,
        bool frozen = false) =>
        new(
            store,
            new StubScheduleReadStore(published),
            // The real schema, not a fixture: these tests are as much about it stating 2026-2027
            // for Grade 2 Turkish as about the service reading it.
            CurrentSupportedProfileSchema.Create(),
            new StubFreezeStore(frozen),
            new ProfileAcademicYearDriftOptions(),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingRolloverStore(IReadOnlyList<ProfileRolloverCandidate> candidates)
        : IProfileAcademicYearRolloverStore
    {
        public List<Guid> Applied { get; } = [];

        public string? AppliedAcademicYear { get; private set; }

        public string? AppliedSchemaVersion { get; private set; }

        public Task<IReadOnlyList<ProfileRolloverCandidate>> ListCandidatesAsync(
            ProfileRolloverScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProfileRolloverCandidate>>(
            [
                .. candidates.Where(candidate =>
                    candidate.Profile.AcademicYear == scope.FromAcademicYear
                    && candidate.Profile.ClassYear == scope.ClassYear
                    && candidate.Profile.ProgramLanguage == scope.ProgramLanguage),
            ]);

        public Task<IReadOnlyList<DriftedProfile>> ListDriftedAsync(
            int classYear,
            ProgramLanguage programLanguage,
            string expectedAcademicYear,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DriftedProfile>>(
            [
                .. candidates
                    .Where(candidate => candidate.Profile.ClassYear == classYear
                        && candidate.Profile.ProgramLanguage == programLanguage
                        && candidate.Profile.AcademicYear != expectedAcademicYear)
                    .Take(limit)
                    .Select(candidate => new DriftedProfile
                    {
                        UserId = candidate.UserId,
                        Profile = candidate.Profile,
                    }),
            ]);

        public Task<ProfileRolloverApplyResult> ApplyAsync(
            IReadOnlyCollection<Guid> userIds,
            string toAcademicYear,
            string toSchemaVersion,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Applied.AddRange(userIds);
            AppliedAcademicYear = toAcademicYear;
            AppliedSchemaVersion = toSchemaVersion;
            return Task.FromResult(new ProfileRolloverApplyResult
            {
                ProfilesMoved = userIds.Count,
                ConvergenceRequested = userIds.Count,
            });
        }
    }

    /// <summary>
    /// Serves the published records and derives live identities from them, so an identity absent
    /// from the list reads as stranded.
    /// </summary>
    private sealed class StubScheduleReadStore(IReadOnlyList<CanonicalScheduleRecord> published)
        : ICanonicalScheduleReadStore
    {
        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListCurrentPublishedRecordsAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CanonicalScheduleRecord>>(
                [.. published.Where(record => record.AcademicYear == academicYear)]);

        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListRecordsByIdsAsync(
            IReadOnlyCollection<Guid> recordIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CanonicalScheduleRecord>>([]);

        public Task<IReadOnlyList<PublishedRecordIdentity>> ListCurrentPublishedIdentitiesAsync(
            string academicYear,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PublishedRecordIdentity>>(
            [
                .. published
                    .Where(record => record.AcademicYear == academicYear)
                    .Select(record => new PublishedRecordIdentity
                    {
                        SourceId = record.SourceId,
                        StableIdentity = record.StableIdentity,
                    }),
            ]);
    }

    private sealed class StubFreezeStore(bool frozen) : IOperationalFreezeStore
    {
        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = frozen });

        public Task<bool> IsFrozenAsync(
            OperationalFreezeScope scope,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<OperationalFreezeSnapshot>> ListScopedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OperationalFreezeSnapshot>>([]);

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool isFrozen,
            string actorEmail,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationalFreezeChangeResult> SetScopedAsync(
            OperationalFreezeScope scope,
            bool isFrozen,
            string actorEmail,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
