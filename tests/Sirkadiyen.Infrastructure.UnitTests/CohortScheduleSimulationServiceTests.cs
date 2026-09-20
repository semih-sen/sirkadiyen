using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Scheduling.Access;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class CohortScheduleSimulationServiceTests
{
    private const string ProgramYear = "2026-2027";

    /// <summary>Monday of the week every dated test below works in.</summary>
    private static readonly DateOnly WeekStart = new(2026, 9, 21);

    [Theory]
    [InlineData(2026, 9, 21)] // Monday: its own week start.
    [InlineData(2026, 9, 23)] // Wednesday.
    [InlineData(2026, 9, 27)] // Sunday: back six days, not forward one.
    public void AnyDayOfTheWeekSnapsBackToItsMonday(int year, int month, int day)
    {
        Assert.Equal(
            WeekStart,
            CohortScheduleSimulationService.SnapToMonday(new DateOnly(year, month, day)));
    }

    [Fact]
    public async Task TheWeekIncludesBothItsEndsAndNothingOutsideThem()
    {
        CohortSimulationWeek week = await SimulateAsync(
        [
            Record(date: WeekStart.AddDays(-1), title: "Before"),
            Record(date: WeekStart, title: "Monday"),
            Record(date: WeekStart.AddDays(6), title: "Sunday"),
            Record(date: WeekStart.AddDays(7), title: "After"),
        ]);

        Assert.Equal(
            ["Monday", "Sunday"],
            week.Events.Select(simulated => simulated.Raw.DisplayTitle));
        Assert.Equal(WeekStart, week.WeekStartLocalDate);
        Assert.Equal(WeekStart.AddDays(6), week.WeekEndLocalDate);
    }

    [Fact]
    public async Task SelectorsOfOneDimensionAreAlternatives()
    {
        // A lesson published to both curriculum groups reaches a student in either (ADR-109).
        CohortSimulationWeek week = await SimulateAsync(
        [
            Record(
                scope: AudienceScope.SelectedGroups,
                selectors: [("curriculumGroup", "3-A"), ("curriculumGroup", "3-B")]),
        ]);

        Assert.Single(week.Events);
    }

    [Fact]
    public async Task SelectorsOfDifferentDimensionsEachNarrowTheAudience()
    {
        // The same cohort is in 3-A but in faculty-practice group A5, so a lesson addressed to
        // 3-A *and* A3 is not theirs. Reading every selector as an alternative is the bug
        // ADR-109 was written for.
        CohortSimulationWeek week = await SimulateAsync(
        [
            Record(
                scope: AudienceScope.SelectedGroups,
                selectors: [("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A3")]),
        ]);

        Assert.Empty(week.Events);
        Assert.Equal(0, week.CohortYearEventCount);
    }

    [Fact]
    public async Task ALessonAddressedToTheWholeProgramAlwaysApplies()
    {
        CohortSimulationWeek week = await SimulateAsync(
            [Record(scope: AudienceScope.AllStudentsInProgram)]);

        Assert.Single(week.Events);
    }

    [Fact]
    public async Task ACancelledLessonNeverAppears()
    {
        CohortSimulationWeek week = await SimulateAsync(
            [Record(status: CanonicalRecordStatus.Cancelled)]);

        Assert.Empty(week.Events);
    }

    [Fact]
    public async Task AnAllDayLessonIsReturnedWithNoTimes()
    {
        CohortSimulationWeek week = await SimulateAsync([Record(allDay: true)]);

        CohortSimulationEvent simulated = Assert.Single(week.Events);
        Assert.True(simulated.IsAllDay);
        Assert.Null(simulated.StartLocalTime);
        Assert.Null(simulated.EndLocalTime);

        // The record covers exactly one local date; Google's exclusive end date is the calendar
        // adapter's conversion and must not leak into a grid.
        Assert.Equal(WeekStart, simulated.LocalDate);
    }

    [Fact]
    public async Task TwoSourcesPublishingOneLessonBothAppear()
    {
        // Two identities are two events on a real calendar. Hiding one here would make the page
        // disagree with the calendar it exists to explain.
        CohortSimulationWeek week = await SimulateAsync(
        [
            Record(sourceId: SourceId.Parse("G3-TR-A-ANNUAL"), stableIdentity: "identity-a"),
            Record(sourceId: SourceId.Parse("G3-TR-A-BEDSIDE"), stableIdentity: "identity-b"),
        ]);

        Assert.Equal(2, week.Events.Count);
        Assert.All(week.Events, simulated => Assert.False(simulated.Raw.SharesStableIdentity));
        Assert.Equal(["G3-TR-A-ANNUAL", "G3-TR-A-BEDSIDE"], week.PublishedSourceIds);
    }

    [Fact]
    public async Task TwoRecordsSharingAStableIdentityAreBothFlagged()
    {
        // The managed event id derives from the stable identity alone, so these would collapse
        // into one event on a calendar. That is a finding, not a duplicate to swallow.
        CohortSimulationWeek week = await SimulateAsync(
        [
            Record(sourceId: SourceId.Parse("G3-TR-A-ANNUAL"), stableIdentity: "shared"),
            Record(sourceId: SourceId.Parse("G3-TR-A-BEDSIDE"), stableIdentity: "shared"),
        ]);

        Assert.Equal(2, week.Events.Count);
        Assert.All(week.Events, simulated => Assert.True(simulated.Raw.SharesStableIdentity));
    }

    [Fact]
    public async Task AnEmptyWeekStillReportsTheYearItBelongsTo()
    {
        // The difference between "quiet week" and "this cohort receives nothing" is the whole
        // reason the year count is carried.
        CohortSimulationWeek week = await SimulateAsync(
        [
            Record(date: WeekStart.AddDays(30)),
            Record(date: WeekStart.AddDays(31)),
        ]);

        Assert.Empty(week.Events);
        Assert.Equal(2, week.CohortYearEventCount);
        Assert.Equal(["G3-TR-A-ANNUAL"], week.PublishedSourceIds);
    }

    [Fact]
    public async Task TheProgramsOwnAcademicYearIsWhatIsResolved()
    {
        // A record stamped with another year is not this cohort's, even in the same class year
        // and language (ADR-103/115).
        RecordingStore store = new(
        [
            Record(),
            Record(academicYear: "2025-2026", title: "LastYear"),
        ]);

        CohortSimulationWeek week = await SimulateAsync(store);

        Assert.Equal(ProgramYear, week.AcademicYear);
        Assert.Equal(ProgramYear, store.RequestedAcademicYear);
        Assert.Single(week.Events);
    }

    [Fact]
    public async Task AnUnsupportedProgramIsRefused()
    {
        CohortSimulationValidationException exception =
            await Assert.ThrowsAsync<CohortSimulationValidationException>(
                () => SimulateAsync([], classYear: 6));

        Assert.Contains(
            exception.Errors,
            error => error.Code == StudentProfileValidationErrorCode.UnsupportedProgram);
    }

    [Fact]
    public async Task AnUnknownSelectorIsRefused()
    {
        CohortSimulationValidationException exception =
            await Assert.ThrowsAsync<CohortSimulationValidationException>(
                () => SimulateAsync(
                    [],
                    selectors: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["curriculumGroup"] = "3-A",
                        ["facultyPracticeGroup"] = "A5",
                        ["anatomyGroup"] = "1",
                    }));

        Assert.Contains(
            exception.Errors,
            error => error.Code == StudentProfileValidationErrorCode.UnknownSelector
                && error.Key == "anatomyGroup");
    }

    [Fact]
    public async Task AnUnsupportedSelectorValueIsRefused()
    {
        CohortSimulationValidationException exception =
            await Assert.ThrowsAsync<CohortSimulationValidationException>(
                () => SimulateAsync(
                    [],
                    selectors: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["curriculumGroup"] = "3-C",
                        ["facultyPracticeGroup"] = "A5",
                    }));

        Assert.Contains(
            exception.Errors,
            error => error.Code == StudentProfileValidationErrorCode.UnsupportedValue);
    }

    [Fact]
    public async Task APartialCohortIsAnsweredRatherThanRefused()
    {
        // An operator narrows a cohort one choice at a time and watches the week fill in, so an
        // unstated dimension is a partial question rather than an invalid one.
        CohortSimulationWeek week = await SimulateAsync(
            [Record()],
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["curriculumGroup"] = "3-A",
            });

        Assert.Single(week.Events);
        Assert.Equal(["facultyPracticeGroup"], week.MissingRequiredSelectors);
    }

    [Fact]
    public async Task StatingNothingYieldsOnlyTheLessonsAddressedToEveryone()
    {
        // With no dimension declared, the audience rule withholds every cohort-scoped lesson
        // (ADR-109) and leaves exactly the programme-wide ones. That is the state the week
        // fills in from as the operator chooses.
        CohortSimulationWeek week = await SimulateAsync(
            [
                Record(scope: AudienceScope.AllStudentsInProgram, title: "Herkese"),
                Record(
                    scope: AudienceScope.SelectedGroups,
                    selectors: [("curriculumGroup", "3-A")],
                    title: "Sadece 3-A"),
            ],
            selectors: new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Equal(["Herkese"], week.Events.Select(simulated => simulated.Raw.DisplayTitle));
        Assert.Equal(["curriculumGroup", "facultyPracticeGroup"], week.MissingRequiredSelectors);
    }

    [Fact]
    public async Task NarrowingOneMoreDimensionAddsTheLessonsItUnlocks()
    {
        List<CanonicalScheduleRecord> published =
        [
            Record(scope: AudienceScope.AllStudentsInProgram, title: "Herkese"),
            Record(
                scope: AudienceScope.SelectedGroups,
                selectors: [("curriculumGroup", "3-A")],
                title: "Sadece 3-A"),
        ];

        CohortSimulationWeek before = await SimulateAsync(
            published,
            selectors: new Dictionary<string, string>(StringComparer.Ordinal));
        CohortSimulationWeek after = await SimulateAsync(
            published,
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["curriculumGroup"] = "3-A",
            });

        Assert.Single(before.Events);
        Assert.Equal(2, after.Events.Count);
        Assert.Equal(["facultyPracticeGroup"], after.MissingRequiredSelectors);
    }

    [Fact]
    public async Task AFullyStatedCohortReportsNothingMissing()
    {
        CohortSimulationWeek week = await SimulateAsync([Record()]);

        Assert.Empty(week.MissingRequiredSelectors);
    }

    [Fact]
    public async Task ADependentSelectorWithoutItsParentIsStillRefused()
    {
        // Allowing a partial cohort does not make an incoherent one acceptable: a faculty
        // cohort means a different rotation in each curriculum group, so it cannot be judged
        // without one (ADR-099).
        CohortSimulationValidationException exception =
            await Assert.ThrowsAsync<CohortSimulationValidationException>(
                () => SimulateAsync(
                    [],
                    selectors: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["facultyPracticeGroup"] = "A5",
                    }));

        Assert.Contains(
            exception.Errors,
            error => error.Code == StudentProfileValidationErrorCode.MissingDependency
                && error.Key == "facultyPracticeGroup");
    }

    [Fact]
    public async Task TheCalendarsOwnPresentationIsWhatIsShown()
    {
        // Guards against the projection quietly growing a second copy of the presentation rules.
        CohortSimulationWeek week = await SimulateAsync(
        [
            Record(
                eventType: ScheduleEventType.BedsidePractice,
                title: "Kardiyoloji",
                location: "Amfi programına bakınız"),
        ]);

        CohortSimulationEvent simulated = Assert.Single(week.Events);
        Assert.Equal("UYGULAMA - KARDİYOLOJİ", simulated.Summary);

        // The policy withholds a pointer to another document, but the raw record still says it,
        // and seeing that difference is what the detail panel is for.
        Assert.Null(simulated.Location);
        Assert.Equal("Amfi programına bakınız", simulated.Raw.RawLocation);
    }

    [Fact]
    public async Task TheOperatorsDefaultColoursAreApplied()
    {
        CohortSimulationWeek configured = await SimulateAsync(
            new RecordingStore([Record(departments: ["anatomi"])]),
            colors: TestDepartmentColors.Create(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["anatomi"] = "#123456",
                }));
        CohortSimulationWeek catalogDefault = await SimulateAsync(
            [Record(departments: ["anatomi"])]);

        Assert.Equal("#123456", Assert.Single(configured.Events).Label.BackgroundColor);
        Assert.Equal("#D50000", Assert.Single(catalogDefault.Events).Label.BackgroundColor);
    }

    [Fact]
    public async Task TheStatedSelectorsAreEchoedBackWithTheWeek()
    {
        CohortSimulationWeek week = await SimulateAsync([Record()]);

        Assert.Equal("3-A", week.Selectors["curriculumGroup"]);
        Assert.Equal("A5", week.Selectors["facultyPracticeGroup"]);
        Assert.Equal(3, week.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, week.ProgramLanguage);
        Assert.Equal("Europe/Istanbul", week.TimeZoneId);
    }

    [Fact]
    public async Task TheAudienceSelectorsAreProjectedAsTypedPairs()
    {
        // The browser is never handed a canonical string to parse (AI_GUIDELINE §5).
        CohortSimulationWeek week = await SimulateAsync(
        [
            Record(
                scope: AudienceScope.SelectedGroups,
                selectors: [("curriculumGroup", "3-A")]),
        ]);

        AudienceSelectorView selector =
            Assert.Single(Assert.Single(week.Events).Raw.AudienceSelectors);
        Assert.Equal("curriculumGroup", selector.Dimension);
        Assert.Equal("3-A", selector.Value);
    }

    private static Task<CohortSimulationWeek> SimulateAsync(
        IReadOnlyList<CanonicalScheduleRecord> published,
        int classYear = 3,
        IReadOnlyDictionary<string, string>? selectors = null) =>
        SimulateAsync(new RecordingStore(published), classYear, selectors);

    private static async Task<CohortSimulationWeek> SimulateAsync(
        RecordingStore store,
        int classYear = 3,
        IReadOnlyDictionary<string, string>? selectors = null,
        DepartmentColorService? colors = null)
    {
        CohortScheduleSimulationService service = new(
            store,
            colors ?? TestDepartmentColors.Create(),
            Schema());

        return await service.SimulateAsync(
            new CohortSimulationQuery
            {
                ClassYear = classYear,
                ProgramLanguage = ProgramLanguage.Turkish,
                Selectors = selectors ?? new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["curriculumGroup"] = "3-A",
                    ["facultyPracticeGroup"] = "A5",
                },
                // A Wednesday, so the snapping is exercised by every case rather than only the
                // one that names it.
                AnchorLocalDate = WeekStart.AddDays(2),
            },
            CancellationToken.None);
    }

    /// <summary>
    /// A schema of its own rather than the shipped one, so these tests keep stating what they
    /// mean after the next academic-year rollover moves the real programs.
    /// </summary>
    private static SupportedProfileSchema Schema() => new()
    {
        AcademicYear = ProgramYear,
        SchemaVersion = "test",
        Programs =
        [
            new SupportedProfileProgram
            {
                AcademicYear = ProgramYear,
                ClassYear = 3,
                ProgramLanguage = ProgramLanguage.Turkish,
                Dimensions =
                [
                    new SupportedProfileDimension
                    {
                        Key = "curriculumGroup",
                        Required = true,
                        Values = ["3-A", "3-B"],
                    },
                    new SupportedProfileDimension
                    {
                        Key = "facultyPracticeGroup",
                        Required = true,
                        DependsOn = "curriculumGroup",
                        ValuesByParent =
                            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                            {
                                ["3-A"] = ["A3", "A5"],
                                ["3-B"] = ["B3", "B5"],
                            },
                    },
                ],
            },
        ],
    };

    private static CanonicalScheduleRecord Record(
        AudienceScope scope = AudienceScope.AllStudentsInProgram,
        IReadOnlyList<(string Dimension, string Value)>? selectors = null,
        string academicYear = ProgramYear,
        CanonicalRecordStatus status = CanonicalRecordStatus.Scheduled,
        bool allDay = false,
        DateOnly? date = null,
        string title = "Lesson",
        string? location = null,
        IReadOnlyList<string>? departments = null,
        ScheduleEventType eventType = ScheduleEventType.Theory,
        SourceId? sourceId = null,
        string? stableIdentity = null) =>
        CalendarTestData.Record(
            scope: scope,
            selectors: selectors,
            classYear: 3,
            programLanguage: ProgramLanguage.Turkish,
            academicYear: academicYear,
            status: status,
            allDay: allDay,
            date: date ?? WeekStart,
            stableIdentity: stableIdentity,
            displayTitle: title,
            location: location,
            departments: departments,
            eventType: eventType,
            sourceId: sourceId ?? SourceId.Parse("G3-TR-A-ANNUAL"));

    /// <summary>
    /// Stands in for the published schedule and remembers what it was asked for, so a test can
    /// assert on the academic year the service derived rather than only on what came back.
    /// </summary>
    private sealed class RecordingStore(IReadOnlyList<CanonicalScheduleRecord> published)
        : ICanonicalScheduleReadStore
    {
        public string? RequestedAcademicYear { get; private set; }

        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListCurrentPublishedRecordsAsync(
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            CancellationToken cancellationToken)
        {
            RequestedAcademicYear = academicYear;

            // The real store filters on the program dimensions in SQL; mirroring that here keeps
            // the fake from being more generous than the thing it stands in for.
            return Task.FromResult<IReadOnlyList<CanonicalScheduleRecord>>(
            [
                .. published.Where(record =>
                    record.AcademicYear == academicYear
                    && record.ClassYear == classYear
                    && record.ProgramLanguage == programLanguage
                    && record.RecordStatus == CanonicalRecordStatus.Scheduled),
            ]);
        }

        public Task<IReadOnlyList<CanonicalScheduleRecord>> ListRecordsByIdsAsync(
            IReadOnlyCollection<Guid> recordIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CanonicalScheduleRecord>>([]);

        public Task<IReadOnlyList<PublishedRecordIdentity>> ListCurrentPublishedIdentitiesAsync(
            string academicYear,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PublishedRecordIdentity>>([]);
    }
}
