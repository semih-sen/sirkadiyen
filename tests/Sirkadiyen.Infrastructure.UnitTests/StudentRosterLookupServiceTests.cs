using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers turning what a student list states into profile suggestions (ADR-085):
/// what is offered, what is withheld, and what is never decided.
/// </summary>
public sealed class StudentRosterLookupServiceTests
{
    private static readonly SupportedProfileSchema Schema = CurrentSupportedProfileSchema.Create();

    [Fact]
    public async Task AMatchSuggestsWhatTheListStatesAndNamesWhatItDoesNotAsync()
    {
        // The real Grade 2 Turkish case: the list states the practice group and
        // subgroup, and says nothing about the anatomy rotation, which the program
        // also requires.
        StudentRosterLookupService service = Service(Grade2TurkishReading(
            Entry("0101250001", "HAY*******", "KIY***", ("practiceGroup", "A"), ("practiceSubgroup", "A1"))));

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0101250001",
            CancellationToken.None);

        Assert.Equal(StudentRosterLookupOutcome.Matched, result.Outcome);
        Assert.Equal(2, result.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, result.ProgramLanguage);
        Assert.Equal("HAY*******", result.GivenName);
        Assert.Equal(
            new Dictionary<string, string> { ["practiceGroup"] = "A", ["practiceSubgroup"] = "A1" },
            result.SuggestedSelectors);
        Assert.Equal(["anatomyGroup"], result.DimensionsRequiringInput);
        StudentRosterLookupNotice notice = Assert.Single(result.Notices);
        Assert.Equal(StudentRosterLookupNoticeCode.DimensionNotStatedByRoster, notice.Code);
        Assert.Equal("anatomyGroup", notice.Dimension);
    }

    [Fact]
    public async Task ANumberOnTwoListsIsNeverResolvedToOneOfThemAsync()
    {
        // Not hypothetical: 0101240080 is on both the Grade 2 and the Grade 3
        // Turkish list as published.
        StudentRosterLookupService service = Service(
            Grade2TurkishReading(Entry("0101240080", "BİR", "ÖĞRENCİ", ("practiceGroup", "A"))),
            Grade3TurkishReading(Entry("0101240080", "BİR", "ÖĞRENCİ", ("curriculumGroup", "3-A"))));

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0101240080",
            CancellationToken.None);

        Assert.Equal(StudentRosterLookupOutcome.Ambiguous, result.Outcome);
        Assert.Equal(["G2-TR-ROSTER", "G3-TR-ROSTER"], result.ConflictingRosterIds);
        Assert.Empty(result.SuggestedSelectors);
        Assert.Null(result.GivenName);
        Assert.Null(result.ClassYear);
    }

    [Fact]
    public async Task ANumberTwiceInOneListIsAmbiguousTooAsync()
    {
        StudentRosterLookupService service = Service(Grade2TurkishReading(
            Entry("0101250001", "BİR", "ÖĞRENCİ", ("practiceGroup", "A")),
            Entry("0101250001", "İKİ", "ÖĞRENCİ", ("practiceGroup", "B"))));

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0101250001",
            CancellationToken.None);

        Assert.Equal(StudentRosterLookupOutcome.Ambiguous, result.Outcome);
        Assert.Equal(["G2-TR-ROSTER"], result.ConflictingRosterIds);
    }

    [Fact]
    public async Task AProgramTheSchemaDoesNotDeclareConfirmsIdentityAndSuggestsNothingAsync()
    {
        // Grade 2 English is deliberately closed until its audience paths are
        // parser-complete (ADR-084), and Grade 3 English declares no selector at
        // all (ADR-098). A list existing does not open a program.
        StudentRosterLookupService service = Service(new StudentRosterReading
        {
            RosterId = "G2-EN-ROSTER",
            AcademicYear = "2026-2027",
            ClassYear = 2,
            ProgramLanguage = ProgramLanguage.English,
            Entries =
            [
                Entry("0102250001", "ZEY*********", "SEY***", ("generalGroup", "İ1"), ("generalSubgroup", "i1")),
            ],
        });

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0102250001",
            CancellationToken.None);

        Assert.Equal(StudentRosterLookupOutcome.Matched, result.Outcome);
        Assert.Equal("ZEY*********", result.GivenName);
        Assert.Empty(result.SuggestedSelectors);
        Assert.Empty(result.DimensionsRequiringInput);
        Assert.Equal(
            StudentRosterLookupNoticeCode.ProgramNotOnboardable,
            Assert.Single(result.Notices).Code);
    }

    [Fact]
    public async Task AListLeftOnLastYearSuggestsNothingAsync()
    {
        // The ADR-115 failure in roster form: a list nobody rolled over would
        // otherwise prefill last year's cohorts into this year's profile.
        StudentRosterLookupService service = Service(Grade2TurkishReading(
            Entry("0101250001", "BİR", "ÖĞRENCİ", ("practiceGroup", "A"))) with
        {
            AcademicYear = "2025-2026",
        });

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0101250001",
            CancellationToken.None);

        Assert.Empty(result.SuggestedSelectors);
        Assert.Equal(
            StudentRosterLookupNoticeCode.RosterYearDiffersFromProgram,
            Assert.Single(result.Notices).Code);
    }

    [Fact]
    public async Task AValueTheProgramDoesNotAllowIsExplainedRatherThanOfferedAsync()
    {
        // A suggestion the profile validator would reject is worse than none: the
        // student would confirm it and be told their own faculty list is invalid.
        StudentRosterLookupService service = Service(Grade2TurkishReading(
            Entry("0101250001", "BİR", "ÖĞRENCİ", ("practiceGroup", "Z"), ("practiceSubgroup", "Z1"))));

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0101250001",
            CancellationToken.None);

        Assert.Empty(result.SuggestedSelectors);
        Assert.Equal(
            ["practiceGroup", "practiceSubgroup", "anatomyGroup"],
            result.DimensionsRequiringInput);
        Assert.Equal(
            2,
            result.Notices.Count(notice =>
                notice.Code == StudentRosterLookupNoticeCode.ValueNotSupportedByProgram));
    }

    [Fact]
    public async Task ASubgroupIsRejectedWithTheGroupItDependsOnAsync()
    {
        // 'A1' means nothing without 'A'. Checking the subgroup against the raw
        // stated group rather than the accepted one would suggest a pair the
        // schema never allows together.
        StudentRosterLookupService service = Service(Grade2TurkishReading(
            Entry("0101250001", "BİR", "ÖĞRENCİ", ("practiceGroup", "Z"), ("practiceSubgroup", "A1"))));

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0101250001",
            CancellationToken.None);

        Assert.Empty(result.SuggestedSelectors);
    }

    [Fact]
    public async Task ADimensionTheProgramNeverDeclaredIsReportedAsync()
    {
        StudentRosterLookupService service = Service(Grade3TurkishReading(
            Entry(
                "0101240001",
                "BİR",
                "ÖĞRENCİ",
                ("curriculumGroup", "3-A"),
                ("practiceSubgroup", "A1"))));

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0101240001",
            CancellationToken.None);

        Assert.Equal("3-A", result.SuggestedSelectors["curriculumGroup"]);
        Assert.Contains(
            result.Notices,
            notice => notice.Code == StudentRosterLookupNoticeCode.DimensionNotDeclaredByProgram
                && notice.Dimension == "practiceSubgroup");
    }

    [Fact]
    public async Task AMissIsAMissAndSaysWhetherAListCouldNotBeReadAsync()
    {
        StudentRosterLookupService service = new(
            new FakeIndex(new StudentRosterIndexSnapshot
            {
                ReadAtUtc = DateTimeOffset.UnixEpoch,
                Readings = [],
                Failures = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["G2-TR-ROSTER"] = "Google returned 503.",
                },
            }),
            Schema);

        StudentRosterLookupResult result = await service.LookUpAsync(
            "0101250001",
            CancellationToken.None);

        Assert.Equal(StudentRosterLookupOutcome.NotFound, result.Outcome);
        Assert.Equal(["G2-TR-ROSTER"], result.UnreadableRosterIds);
        Assert.Null(result.GivenName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("01012500a1")]
    public async Task AMalformedNumberIsRefusedBeforeAnyListIsReadAsync(string number)
    {
        StudentRosterLookupService service = Service(Grade2TurkishReading());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.LookUpAsync(number, CancellationToken.None));
    }

    private static StudentRosterLookupService Service(params StudentRosterReading[] readings) =>
        new(
            new FakeIndex(new StudentRosterIndexSnapshot
            {
                ReadAtUtc = DateTimeOffset.UnixEpoch,
                Readings = readings,
            }),
            Schema);

    private static StudentRosterReading Grade2TurkishReading(params StudentRosterEntry[] entries) =>
        new()
        {
            RosterId = "G2-TR-ROSTER",
            AcademicYear = CurrentSupportedProfileSchema.AcademicYear,
            ClassYear = 2,
            ProgramLanguage = ProgramLanguage.Turkish,
            Entries = entries,
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
        string givenName,
        string familyName,
        params (string Key, string Value)[] selectors) => new()
        {
            StudentNumber = studentNumber,
            GivenName = givenName,
            FamilyName = familyName,
            RowNumber = 2,
            Selectors = selectors.ToDictionary(
                selector => selector.Key,
                selector => selector.Value,
                StringComparer.Ordinal),
        };

    private sealed class FakeIndex(StudentRosterIndexSnapshot snapshot) : IStudentRosterIndex
    {
        public Task<StudentRosterIndexSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }
}
