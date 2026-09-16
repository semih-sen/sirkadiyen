using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers checking a cohort's stored profiles against the published lists and correcting the ones
/// that disagree (ADR-159): what is flagged, what is left alone, and what the apply actually writes.
/// </summary>
public sealed class RosterProfileAuditServiceTests
{
    private static readonly SupportedProfileSchema Schema = CurrentSupportedProfileSchema.Create();

    private static readonly Guid WrongGroup = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid RightGroup = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid NoGroupYet = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid NotOnList = Guid.Parse("00000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task PlanFlagsAWrongGroupLeavesACorrectOneAndReportsAnUnresolvedNumberAsync()
    {
        // Three students the list resolves and one it does not: one entered the wrong faculty group,
        // one entered it right, one never entered it (the case the new column exists to fill), and
        // one is on no list at all.
        RosterProfileAuditService service = Service(
            profiles:
            [
                Profile(WrongGroup, "0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A2"), ("microPathologyGroup", "A1")),
                Profile(RightGroup, "0101240002", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A5"), ("microPathologyGroup", "A1")),
                Profile(NoGroupYet, "0101240003", ("curriculumGroup", "3-A"), ("microPathologyGroup", "A1")),
                Profile(NotOnList, "0101249999", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A1"), ("microPathologyGroup", "A1")),
            ],
            readings:
            [
                Grade3TurkishReading(
                    Entry("0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A5")),
                    Entry("0101240002", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A5")),
                    Entry("0101240003", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A5"))),
            ]);

        RosterProfileAuditPlan plan = await service.PlanAsync(
            new RosterProfileAuditScope { ClassYear = 3, ProgramLanguage = ProgramLanguage.Turkish },
            CancellationToken.None);

        Assert.Equal("2026-2027", plan.AcademicYear);
        Assert.Equal(4, plan.ProfilesExamined);
        Assert.Equal(1, plan.ProfilesInAgreement);
        Assert.Equal([NotOnList], plan.UnresolvedByRoster);

        Assert.Equal(2, plan.Users.Count);

        RosterProfileUserPlan wrong = Assert.Single(plan.Users, user => user.UserId == WrongGroup);
        RosterProfileCorrection wrongCorrection = Assert.Single(wrong.Corrections);
        Assert.Equal("facultyPracticeGroup", wrongCorrection.Dimension);
        Assert.Equal("A2", wrongCorrection.StoredValue);
        Assert.Equal("A5", wrongCorrection.RosterValue);

        RosterProfileUserPlan missing = Assert.Single(plan.Users, user => user.UserId == NoGroupYet);
        RosterProfileCorrection missingCorrection = Assert.Single(missing.Corrections);
        Assert.Null(missingCorrection.StoredValue);
        Assert.Equal("A5", missingCorrection.RosterValue);
    }

    [Fact]
    public async Task ARosterThatCouldNotBeReadDrivesNoCorrectionAndIsReportedAsync()
    {
        // A stale list confirms nothing. The student's stored value is left exactly as it is, and
        // the operator is told which list was unreadable when they read the plan.
        RosterProfileAuditService service = new(
            new FakeAuditStore(
            [
                Profile(WrongGroup, "0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A2"), ("microPathologyGroup", "A1")),
            ]),
            new StudentRosterLookupService(
                new FakeIndex(new StudentRosterIndexSnapshot
                {
                    ReadAtUtc = DateTimeOffset.UnixEpoch,
                    Readings = [],
                    Failures = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["G3-TR-ROSTER"] = "Google returned 503.",
                    },
                }),
                Schema),
            NeverSavesProfileService(),
            Schema,
            new StubFreezeStore(isFrozen: false));

        RosterProfileAuditPlan plan = await service.PlanAsync(
            new RosterProfileAuditScope { ClassYear = 3, ProgramLanguage = ProgramLanguage.Turkish },
            CancellationToken.None);

        Assert.Empty(plan.Users);
        Assert.Equal([WrongGroup], plan.UnresolvedByRoster);
        Assert.Equal(["G3-TR-ROSTER"], plan.UnreadableRosterIds);
    }

    [Fact]
    public async Task RequestCorrectsThroughTheStudentWritePathAndReportsResyncAsync()
    {
        RecordingProfileStore profileStore = new(audienceChanged: true, calendarResyncRequested: true);
        RosterProfileAuditService service = Service(
            profiles:
            [
                Profile(WrongGroup, "0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A2"), ("microPathologyGroup", "A1")),
            ],
            readings:
            [
                Grade3TurkishReading(
                    Entry("0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A5"))),
            ],
            profileStore: profileStore);

        RosterProfileAuditScope scope = new()
        {
            ClassYear = 3,
            ProgramLanguage = ProgramLanguage.Turkish,
        };

        RosterProfileAuditPlan plan = await service.PlanAsync(scope, CancellationToken.None);

        int recorded = 0;
        RosterProfileAuditRequestResult result = await service.RequestAsync(
            scope,
            plan.PlanHash,
            (_, _) => { recorded++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal(RosterProfileAuditOutcome.Corrected, result.Outcome);
        Assert.Equal(1, result.ProfilesCorrected);
        Assert.Equal(1, result.CalendarResyncRequested);
        Assert.Equal(0, result.ProfilesSkipped);

        // The audit is written before the side effect, exactly once for the batch.
        Assert.Equal(1, recorded);

        // The write went through the student's own path with the corrected value overlaid onto the
        // selectors the student already had, not a replacement of the whole profile.
        Assert.Equal("A5", profileStore.LastSelectors!["facultyPracticeGroup"]);
        Assert.Equal("3-A", profileStore.LastSelectors!["curriculumGroup"]);
        Assert.Equal("A1", profileStore.LastSelectors!["microPathologyGroup"]);
    }

    [Fact]
    public async Task RequestRefusesAConfirmationForAPlanThatHasChangedAsync()
    {
        RosterProfileAuditService service = Service(
            profiles:
            [
                Profile(WrongGroup, "0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A2"), ("microPathologyGroup", "A1")),
            ],
            readings:
            [
                Grade3TurkishReading(
                    Entry("0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A5"))),
            ]);

        RosterProfileAuditRequestResult result = await service.RequestAsync(
            new RosterProfileAuditScope { ClassYear = 3, ProgramLanguage = ProgramLanguage.Turkish },
            "not-the-hash-you-were-shown",
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(RosterProfileAuditOutcome.PlanChanged, result.Outcome);
        Assert.Equal(0, result.ProfilesCorrected);
    }

    [Fact]
    public async Task RequestQueuesNothingWhileFrozenAsync()
    {
        RosterProfileAuditService service = new(
            new FakeAuditStore(
            [
                Profile(WrongGroup, "0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A2"), ("microPathologyGroup", "A1")),
            ]),
            new StudentRosterLookupService(
                new FakeIndex(new StudentRosterIndexSnapshot
                {
                    ReadAtUtc = DateTimeOffset.UnixEpoch,
                    Readings =
                    [
                        Grade3TurkishReading(
                            Entry("0101240001", ("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A5"))),
                    ],
                }),
                Schema),
            NeverSavesProfileService(),
            Schema,
            new StubFreezeStore(isFrozen: true));

        int recorded = 0;
        RosterProfileAuditRequestResult result = await service.RequestAsync(
            new RosterProfileAuditScope { ClassYear = 3, ProgramLanguage = ProgramLanguage.Turkish },
            "any-hash",
            (_, _) => { recorded++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal(RosterProfileAuditOutcome.Frozen, result.Outcome);
        Assert.Equal(0, recorded);
    }

    private static RosterProfileAuditService Service(
        StudentProfileView[] profiles,
        StudentRosterReading[] readings,
        IStudentProfileStore? profileStore = null) =>
        new(
            new FakeAuditStore(profiles),
            new StudentRosterLookupService(
                new FakeIndex(new StudentRosterIndexSnapshot
                {
                    ReadAtUtc = DateTimeOffset.UnixEpoch,
                    Readings = readings,
                }),
                Schema),
            new StudentProfileService(
                Schema,
                profileStore ?? new RecordingProfileStore(true, true),
                new StubLicenseStore(UserLicenseState.Active),
                TimeProvider.System),
            Schema,
            new StubFreezeStore(isFrozen: false));

    private static StudentProfileService NeverSavesProfileService() => new(
        Schema,
        new RecordingProfileStore(true, true),
        new StubLicenseStore(UserLicenseState.Active),
        TimeProvider.System);

    private static StudentProfileView Profile(
        Guid userId,
        string studentNumber,
        params (string Key, string Value)[] selectors) => new()
        {
            UserId = userId,
            AcademicYear = "2026-2027",
            ClassYear = 3,
            ProgramLanguage = ProgramLanguage.Turkish,
            StudentNumber = studentNumber,
            SelectorSchemaVersion = CurrentSupportedProfileSchema.SchemaVersion,
            Selectors = selectors.ToDictionary(
                selector => selector.Key,
                selector => selector.Value,
                StringComparer.Ordinal),
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };

    private static StudentRosterReading Grade3TurkishReading(params StudentRosterEntry[] entries) =>
        new()
        {
            RosterId = "G3-TR-ROSTER",
            AcademicYear = CurrentSupportedProfileSchema.AcademicYear,
            ClassYear = 3,
            ProgramLanguage = ProgramLanguage.Turkish,
            Entries = entries,
        };

    private static StudentRosterEntry Entry(
        string studentNumber,
        params (string Key, string Value)[] selectors) => new()
        {
            StudentNumber = studentNumber,
            GivenName = "BİR",
            FamilyName = "ÖĞRENCİ",
            RowNumber = 2,
            Selectors = selectors.ToDictionary(
                selector => selector.Key,
                selector => selector.Value,
                StringComparer.Ordinal),
        };

    private sealed class FakeAuditStore(IReadOnlyList<StudentProfileView> profiles)
        : IRosterProfileAuditStore
    {
        public Task<IReadOnlyList<StudentProfileView>> ListCohortProfilesAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult(profiles);
    }

    private sealed class FakeIndex(StudentRosterIndexSnapshot snapshot) : IStudentRosterIndex
    {
        public Task<StudentRosterIndexSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);

        public void Invalidate()
        {
        }
    }

    private sealed class RecordingProfileStore(bool audienceChanged, bool calendarResyncRequested)
        : IStudentProfileStore
    {
        public IReadOnlyDictionary<string, string>? LastSelectors { get; private set; }

        public Task<StudentProfileView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StudentProfileView?>(null);

        public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<StudentProfileUpsertResult> UpsertAsync(
            Guid userId,
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            string studentNumber,
            string selectorSchemaVersion,
            IReadOnlyDictionary<string, string> selectors,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            LastSelectors = selectors;
            return Task.FromResult(new StudentProfileUpsertResult
            {
                Profile = new StudentProfileView
                {
                    UserId = userId,
                    AcademicYear = academicYear,
                    ClassYear = classYear,
                    ProgramLanguage = programLanguage,
                    StudentNumber = studentNumber,
                    SelectorSchemaVersion = selectorSchemaVersion,
                    Selectors = selectors,
                    UpdatedAtUtc = atUtc,
                },
                AudienceChanged = audienceChanged,
                CalendarResyncRequested = calendarResyncRequested,
            });
        }
    }

    private sealed class StubFreezeStore(bool isFrozen) : IOperationalFreezeStore
    {
        public Task<OperationalFreezeSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalFreezeSnapshot { IsFrozen = isFrozen });

        public Task<OperationalFreezeChangeResult> SetAsync(
            bool isFrozen,
            string changedBy,
            string reason,
            string correlationId,
            DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubLicenseStore(UserLicenseState state) : ILicenseStore
    {
        public Task<UserLicenseState> GetUserLicenseStateAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(state);

        public Task SaveCreatedAsync(License license, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LicenseRedemptionResult> RedeemAsync(
            byte[] codeHash,
            Guid userId,
            string userEmail,
            DateTimeOffset redeemedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LicenseRevocationResult> RevokeAsync(
            Guid licenseId,
            Guid actorUserId,
            string actorEmail,
            string reason,
            DateTimeOffset revokedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManualLicenseActivationResult> ActivateManuallyAsync(
            Guid userId,
            Guid actorUserId,
            string actorEmail,
            string reason,
            DateTimeOffset activatedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserLicenseSummary?> GetUserLicenseSummaryAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
