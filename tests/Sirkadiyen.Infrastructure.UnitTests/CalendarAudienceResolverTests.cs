using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class CalendarAudienceResolverTests
{
    [Fact]
    public void AProgramWideLessonAppliesToEveryStudentInTheProgram()
    {
        StudentProfileView profile = CalendarTestData.Profile();
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.AllStudentsInProgram);

        Assert.True(CalendarAudienceResolver.Applies(record, profile));
    }

    [Fact]
    public void ACohortLessonAppliesWhenTheStudentBelongsToTheGroup()
    {
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = "A",
                ["practiceSubgroup"] = "A1",
            });
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("practiceGroup", "A")]);

        Assert.True(CalendarAudienceResolver.Applies(record, profile));
    }

    [Fact]
    public void ACohortLessonDoesNotApplyToAStudentInAnotherGroup()
    {
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = "B",
            });
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("practiceGroup", "A")]);

        Assert.False(CalendarAudienceResolver.Applies(record, profile));
    }

    [Fact]
    public void ALessonSharedBySeveralGroupsAppliesIfAnyMatchesTheStudent()
    {
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = "C",
            });
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("practiceGroup", "A"), ("practiceGroup", "C")]);

        Assert.True(CalendarAudienceResolver.Applies(record, profile));
    }

    [Fact]
    public void AMultiDimensionLessonAppliesOnlyToTheStudentsOwnSubgroup()
    {
        // The faculty-practice rotation writes eight cohorts per curriculum group at the same
        // date and time. Matching on the curriculum group alone put all eight on one student's
        // calendar, so every stated dimension has to agree (ADR-109).
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["curriculumGroup"] = "3-A",
                ["facultyPracticeGroup"] = "A3",
            });

        CanonicalScheduleRecord own = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A3")]);
        CanonicalScheduleRecord anotherCohort = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A5")]);

        Assert.True(CalendarAudienceResolver.Applies(own, profile));
        Assert.False(CalendarAudienceResolver.Applies(anotherCohort, profile));
    }

    [Fact]
    public void AMultiDimensionLessonDoesNotApplyWhenOnlyTheSubgroupMatches()
    {
        // The cohort number repeats across curriculum groups, so the narrower dimension may
        // never carry a match on its own.
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["curriculumGroup"] = "3-B",
                ["facultyPracticeGroup"] = "A3",
            });
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A3")]);

        Assert.False(CalendarAudienceResolver.Applies(record, profile));
    }

    [Fact]
    public void ALessonNamingADimensionTheStudentHasNotDeclaredAppliesToNobody()
    {
        // An undeclared dimension cannot be confirmed either way, and a lesson is never
        // widened to a student whose membership is unknown.
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["curriculumGroup"] = "3-A",
            });
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("curriculumGroup", "3-A"), ("facultyPracticeGroup", "A3")]);

        Assert.False(CalendarAudienceResolver.Applies(record, profile));
    }

    [Fact]
    public void SeveralValuesOfOneDimensionStillEnumerateAlternatives()
    {
        // A session both curriculum groups attend states both, and a student in either one
        // receives it. Narrowing across dimensions must not narrow within one (ADR-109).
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["curriculumGroup"] = "3-B",
                ["facultyPracticeGroup"] = "B4",
            });
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("curriculumGroup", "3-A"), ("curriculumGroup", "3-B")]);

        Assert.True(CalendarAudienceResolver.Applies(record, profile));
    }

    [Fact]
    public void ACohortLessonWithNoNamedGroupAppliesToNobody()
    {
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = "A",
            });
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: []);

        Assert.False(CalendarAudienceResolver.Applies(record, profile));
    }

    [Theory]
    [InlineData(2, ProgramLanguage.Turkish, CalendarTestData.AcademicYear)]
    [InlineData(1, ProgramLanguage.English, CalendarTestData.AcademicYear)]
    [InlineData(1, ProgramLanguage.Turkish, "2024-2025")]
    public void ALessonForAnotherProgramNeverApplies(
        int classYear,
        ProgramLanguage programLanguage,
        string academicYear)
    {
        StudentProfileView profile = CalendarTestData.Profile();
        CanonicalScheduleRecord record = CalendarTestData.Record(
            classYear: classYear,
            programLanguage: programLanguage,
            academicYear: academicYear);

        Assert.False(CalendarAudienceResolver.Applies(record, profile));
    }

    [Fact]
    public void ACancelledLessonNeverApplies()
    {
        StudentProfileView profile = CalendarTestData.Profile();
        CanonicalScheduleRecord record = CalendarTestData.Record(
            status: CanonicalRecordStatus.Cancelled);

        Assert.False(CalendarAudienceResolver.Applies(record, profile));
    }
}
