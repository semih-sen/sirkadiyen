using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the repair that removes what a corrected audience rule shows was never a student's
/// (ADR-111). The bug it was built for is the concrete case: eight faculty-practice cohorts
/// written to one Grade 3 student before ADR-109 narrowed the match.
/// </summary>
public sealed class CohortCalendarRepairServiceTests
{
    private static readonly SourceId FacultySource = SourceId.Parse("G3-TR-A-FACULTY");
    private static readonly CohortRepairScope Grade3Turkish = new()
    {
        AcademicYear = "2026-2027",
        ClassYear = 3,
        ProgramLanguage = ProgramLanguage.Turkish,
    };

    [Fact]
    public async Task ThePlanCountsTheCohortEventsThatAreNoLongerTheStudentsAsync()
    {
        // The A3 student holds all eight cohorts' sessions. Seven are surplus.
        StudentProfileView profile = Grade3Profile("A3");
        List<CanonicalScheduleRecord> published =
            [.. Enumerable.Range(1, 8).Select(cohort => FacultyRecord($"A{cohort}"))];

        CohortCalendarRepairService service = Service(
            published,
            Holding(profile, published));

        CohortRepairPlan plan = await service.PlanAsync(Grade3Turkish, CancellationToken.None);

        CohortRepairUserPlan user = Assert.Single(plan.Users);
        Assert.Equal(7, user.SurplusEventCount);
        Assert.Equal(0, user.MissingEventCount);
        Assert.Equal(0, user.UntouchableRetiredCount);
        Assert.Equal(7, plan.TotalSurplusEvents);
        Assert.Equal(1, plan.CohortUserCount);
    }

    [Fact]
    public async Task AnEventWhoseLessonIsNoLongerPublishedIsCountedAndLeftAloneAsync()
    {
        // ADR-089: removing this would be deleting from absence rather than from a published
        // decision. Retiring it stays the semantic diff's job, so the repair only reports it.
        StudentProfileView profile = Grade3Profile("A3");
        CanonicalScheduleRecord own = FacultyRecord("A3");

        CohortRepairPlan plan = await Service(
                published: [own],
                Holding(profile, [own], extraHeldIdentities: ["identity-of-a-retired-lesson"]))
            .PlanAsync(Grade3Turkish, CancellationToken.None);

        // Nothing to converge, so the student is not in the actionable list — but the leftover is
        // still reported to the cohort total, or an operator would never learn it exists.
        Assert.Empty(plan.Users);
        Assert.Equal(0, plan.TotalSurplusEvents);
        Assert.Equal(1, plan.TotalUntouchableRetired);
    }

    [Fact]
    public async Task ARetiredLeftoverIsNeverCountedAsSurplusForAnAffectedStudentAsync()
    {
        // The same student both ways round: one live event that is not theirs, and one leftover
        // whose lesson is gone. Only the first may ever be deleted (ADR-089).
        StudentProfileView profile = Grade3Profile("A3");
        CanonicalScheduleRecord own = FacultyRecord("A3");
        CanonicalScheduleRecord other = FacultyRecord("A5");

        CohortRepairPlan plan = await Service(
                published: [own, other],
                Holding(profile, [own, other], extraHeldIdentities: ["identity-of-a-retired-lesson"]))
            .PlanAsync(Grade3Turkish, CancellationToken.None);

        CohortRepairUserPlan user = Assert.Single(plan.Users);
        Assert.Equal(1, user.SurplusEventCount);
        Assert.Equal(1, user.UntouchableRetiredCount);
        Assert.Equal(1, plan.TotalUntouchableRetired);
    }

    [Fact]
    public async Task AStudentWhoseCalendarIsAlreadyCorrectIsNotInThePlanAsync()
    {
        StudentProfileView profile = Grade3Profile("A3");
        CanonicalScheduleRecord own = FacultyRecord("A3");

        CohortRepairPlan plan = await Service(published: [own], Holding(profile, [own]))
            .PlanAsync(Grade3Turkish, CancellationToken.None);

        Assert.Empty(plan.Users);
        Assert.Equal(1, plan.CohortUserCount);
    }

    [Fact]
    public async Task AMissingEventIsPlannedAsWellAsASurplusOneAsync()
    {
        // The operator is authorizing convergence, not deletion alone, so both halves are shown.
        StudentProfileView profile = Grade3Profile("A3");
        CanonicalScheduleRecord own = FacultyRecord("A3");
        CanonicalScheduleRecord other = FacultyRecord("A5");

        CohortRepairPlan plan = await Service(
                published: [own, other],
                Holding(profile, [other]))
            .PlanAsync(Grade3Turkish, CancellationToken.None);

        CohortRepairUserPlan user = Assert.Single(plan.Users);
        Assert.Equal(1, user.SurplusEventCount);
        Assert.Equal(1, user.MissingEventCount);
    }

    [Fact]
    public async Task ConfirmingThePlanFlagsExactlyTheAffectedConnectionsAsync()
    {
        StudentProfileView affected = Grade3Profile("A3");
        StudentProfileView correct = Grade3Profile("A5");
        CanonicalScheduleRecord a3 = FacultyRecord("A3");
        CanonicalScheduleRecord a5 = FacultyRecord("A5");

        RecordingRepairStore store = new(
            [Holding(affected, [a3, a5]), Holding(correct, [a5])]);
        CohortCalendarRepairService service = Service([a3, a5], store);

        CohortRepairPlan plan = await service.PlanAsync(Grade3Turkish, CancellationToken.None);
        CohortRepairRequestResult result =
            await service.RequestAsync(Grade3Turkish, plan.PlanHash, CancellationToken.None);

        Assert.Equal(CohortRepairOutcome.Requested, result.Outcome);
        Assert.Equal([affected.UserId], store.Requested);
    }

    [Fact]
    public async Task AStalePlanHashIsRefusedAndNothingIsFlaggedAsync()
    {
        // The confirmation authorizes a plan. If the cohort moved between preview and confirm,
        // the operator has not seen what they would be authorizing.
        StudentProfileView profile = Grade3Profile("A3");
        CanonicalScheduleRecord a3 = FacultyRecord("A3");
        CanonicalScheduleRecord a5 = FacultyRecord("A5");

        RecordingRepairStore store = new([Holding(profile, [a3, a5])]);
        CohortCalendarRepairService service = Service([a3, a5], store);

        CohortRepairRequestResult result = await service.RequestAsync(
            Grade3Turkish,
            "0000000000000000000000000000000000000000000000000000000000000000",
            CancellationToken.None);

        Assert.Equal(CohortRepairOutcome.PlanChanged, result.Outcome);
        Assert.Empty(store.Requested);
    }

    [Fact]
    public async Task APlanHashChangesWhenTheAffectedStudentsChangeAsync()
    {
        // Two plans of equal size over different students must not share a hash, or confirming
        // one would authorize repairing the other.
        CanonicalScheduleRecord a3 = FacultyRecord("A3");
        CanonicalScheduleRecord a5 = FacultyRecord("A5");

        CohortRepairPlan first = await Service(
                [a3, a5],
                Holding(Grade3Profile("A3"), [a3, a5]))
            .PlanAsync(Grade3Turkish, CancellationToken.None);
        CohortRepairPlan second = await Service(
                [a3, a5],
                Holding(Grade3Profile("A5"), [a3, a5]))
            .PlanAsync(Grade3Turkish, CancellationToken.None);

        Assert.Equal(first.TotalSurplusEvents, second.TotalSurplusEvents);
        Assert.NotEqual(first.PlanHash, second.PlanHash);
    }

    [Fact]
    public async Task AFrozenProgramQueuesNoRepairAsync()
    {
        // Queueing work a freeze exists to prevent would defer the writes rather than decline
        // them, which is not what freezing a program asked for (ADR-034/043).
        StudentProfileView profile = Grade3Profile("A3");
        CanonicalScheduleRecord a3 = FacultyRecord("A3");
        CanonicalScheduleRecord a5 = FacultyRecord("A5");

        RecordingRepairStore store = new([Holding(profile, [a3, a5])]);
        CohortCalendarRepairService service = Service([a3, a5], store, frozen: true);

        CohortRepairRequestResult result = await service.RequestAsync(
            Grade3Turkish,
            "irrelevant-because-the-freeze-is-checked-first",
            CancellationToken.None);

        Assert.Equal(CohortRepairOutcome.Frozen, result.Outcome);
        Assert.Empty(store.Requested);
    }

    [Fact]
    public async Task AnEmptyPlanFlagsNobodyAsync()
    {
        CanonicalScheduleRecord a3 = FacultyRecord("A3");
        RecordingRepairStore store = new([Holding(Grade3Profile("A3"), [a3])]);
        CohortCalendarRepairService service = Service([a3], store);

        CohortRepairPlan plan = await service.PlanAsync(Grade3Turkish, CancellationToken.None);
        CohortRepairRequestResult result =
            await service.RequestAsync(Grade3Turkish, plan.PlanHash, CancellationToken.None);

        Assert.Equal(CohortRepairOutcome.NothingToRepair, result.Outcome);
        Assert.Empty(store.Requested);
    }

    private static StudentProfileView Grade3Profile(string facultyPracticeGroup) =>
        CalendarTestData.Profile(
            classYear: 3,
            academicYear: "2026-2027",
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["curriculumGroup"] = "3-A",
                ["facultyPracticeGroup"] = facultyPracticeGroup,
            });

    /// <summary>One faculty-practice session: one curriculum group, one cohort within it.</summary>
    private static CanonicalScheduleRecord FacultyRecord(string cohort) =>
        CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("curriculumGroup", "3-A"), ("facultyPracticeGroup", cohort)],
            classYear: 3,
            academicYear: "2026-2027",
            eventType: ScheduleEventType.FacultyPractice,
            sourceId: FacultySource,
            stableIdentity: $"identity-{cohort}");

    private static CohortRepairHolding Holding(
        StudentProfileView profile,
        IReadOnlyList<CanonicalScheduleRecord> held,
        IReadOnlyList<string>? extraHeldIdentities = null) => new()
        {
            UserId = profile.UserId,
            Profile = profile,
            Mappings =
            [
                .. held.Select(record => Mapping(profile.UserId, record.StableIdentity)),
                .. (extraHeldIdentities ?? [])
                    .Select(identity => Mapping(profile.UserId, identity)),
            ],
        };

    private static CalendarEventMappingView Mapping(Guid userId, string stableIdentity) => new()
    {
        UserId = userId,
        StableIdentity = stableIdentity,
        SourceId = FacultySource,
        GoogleCalendarId = "calendar",
        GoogleEventId = $"event-{stableIdentity}",
        ContentHash = "sha256:content",
        CanonicalRecordId = Guid.CreateVersion7(),
    };

    private static CohortCalendarRepairService Service(
        IReadOnlyList<CanonicalScheduleRecord> published,
        params CohortRepairHolding[] holdings) =>
        Service(published, new RecordingRepairStore(holdings));

    private static CohortCalendarRepairService Service(
        IReadOnlyList<CanonicalScheduleRecord> published,
        RecordingRepairStore store,
        bool frozen = false) =>
        new(
            store,
            new StubScheduleReadStore(published),
            new StubFreezeStore(frozen),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingRepairStore(IReadOnlyList<CohortRepairHolding> holdings)
        : ICohortCalendarRepairStore
    {
        public List<Guid> Requested { get; } = [];

        public Task<IReadOnlyList<CohortRepairHolding>> ListCohortHoldingsAsync(
            CohortRepairScope scope,
            CancellationToken cancellationToken) => Task.FromResult(holdings);

        public Task<int> RequestConvergenceAsync(
            IReadOnlyCollection<Guid> userIds,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Requested.AddRange(userIds);
            return Task.FromResult(userIds.Count);
        }
    }

    /// <summary>
    /// Serves the published records, and derives live identities from them — which is what makes
    /// an identity absent from this list read as retired.
    /// </summary>
    private sealed class StubScheduleReadStore(IReadOnlyList<CanonicalScheduleRecord> published)
        : ICanonicalScheduleReadStore
    {
        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListCurrentPublishedRecordsAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            CancellationToken cancellationToken) => Task.FromResult(published);

        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListRecordsByIdsAsync(
            IReadOnlyCollection<Guid> recordIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CanonicalScheduleRecord>>([]);

        public Task<IReadOnlyList<PublishedRecordIdentity>> ListCurrentPublishedIdentitiesAsync(
            string academicYear,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PublishedRecordIdentity>>(
            [
                .. published.Select(record => new PublishedRecordIdentity
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
            CancellationToken cancellationToken) => Task.FromResult(frozen);

        public Task<IReadOnlyList<OperationalFreezeSnapshot>> ListScopedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OperationalFreezeSnapshot>>([]);

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool isFrozen,
            string actor,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationalFreezeChangeResult> SetScopedAsync(
            OperationalFreezeScope scope,
            bool isFrozen,
            string actor,
            string reason,
            string correlationId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
