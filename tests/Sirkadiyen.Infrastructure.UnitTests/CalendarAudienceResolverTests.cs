using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
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
