using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.SchedulePublication;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The pure per-user rules that turn a changed lesson into a calendar operation (ADR-059). The
/// mapping ledger decides who already holds a lesson; these decide what to do about it.
/// </summary>
public sealed class IncrementalSyncPlannerTests
{
    [Fact]
    public void ACohortUserAnEventNowAppliesToGetsAnInsert()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record();
        StudentProfileView profile = CalendarTestData.Profile();

        Assert.Equal(
            IncrementalCalendarOperation.Insert,
            IncrementalSyncPlanner.PlanForCohortCandidate(record, profile));
    }

    [Fact]
    public void ACohortUserAnEventDoesNotApplyToGetsNothing()
    {
        // A different class year is not this student's lesson, even in the same program.
        CanonicalScheduleRecord record = CalendarTestData.Record(classYear: 2);
        StudentProfileView profile = CalendarTestData.Profile(classYear: 1);

        Assert.Equal(
            IncrementalCalendarOperation.None,
            IncrementalSyncPlanner.PlanForCohortCandidate(record, profile));
    }

    [Fact]
    public void ACohortUserInTheSelectedGroupGetsAnInsert()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("group", "A")]);
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal) { ["group"] = "A" });

        Assert.Equal(
            IncrementalCalendarOperation.Insert,
            IncrementalSyncPlanner.PlanForCohortCandidate(record, profile));
    }

    [Fact]
    public void AHolderWhoseLessonIsUnchangedGetsNothing()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(contentHash: "sha256:same");
        StudentProfileView profile = CalendarTestData.Profile();

        Assert.Equal(
            IncrementalCalendarOperation.None,
            IncrementalSyncPlanner.PlanForExistingHolder(record, profile, "sha256:same"));
    }

    [Fact]
    public void AHolderWhoseLessonContentChangedGetsAPatch()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(contentHash: "sha256:new");
        StudentProfileView profile = CalendarTestData.Profile();

        Assert.Equal(
            IncrementalCalendarOperation.Patch,
            IncrementalSyncPlanner.PlanForExistingHolder(record, profile, "sha256:old"));
    }

    [Fact]
    public void AHolderWhoseLessonNoLongerTargetsThemGetsADelete()
    {
        // The lesson narrowed to a group the student is not in; the content hash is irrelevant.
        CanonicalScheduleRecord record = CalendarTestData.Record(
            scope: AudienceScope.SelectedGroups,
            selectors: [("group", "B")],
            contentHash: "sha256:new");
        StudentProfileView profile = CalendarTestData.Profile(
            selectors: new Dictionary<string, string>(StringComparer.Ordinal) { ["group"] = "A" });

        Assert.Equal(
            IncrementalCalendarOperation.Delete,
            IncrementalSyncPlanner.PlanForExistingHolder(record, profile, "sha256:old"));
    }

    [Fact]
    public void AHolderWhoseLessonWasCancelledGetsADelete()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(
            status: CanonicalRecordStatus.Cancelled);
        StudentProfileView profile = CalendarTestData.Profile();

        Assert.Equal(
            IncrementalCalendarOperation.Delete,
            IncrementalSyncPlanner.PlanForExistingHolder(record, profile, "sha256:content"));
    }
}
