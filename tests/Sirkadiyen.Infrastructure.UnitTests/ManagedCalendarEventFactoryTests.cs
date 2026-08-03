using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.SchedulePublication;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class ManagedCalendarEventFactoryTests
{
    private const string Base32HexAlphabet = "0123456789abcdefghijklmnopqrstuv";

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void ATimedRecordBecomesATimedEventInItsZone()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(
            date: new DateOnly(2026, 3, 1),
            start: new TimeOnly(9, 0),
            end: new TimeOnly(10, 30),
            displayTitle: "Anatomy",
            location: "Amfi 1");

        ManagedCalendarEvent result = ManagedCalendarEventFactory.ToManagedEvent(UserId, record);

        Assert.False(result.IsAllDay);
        Assert.Equal(new DateTime(2026, 3, 1, 9, 0, 0), result.LocalStart);
        Assert.Equal(new DateTime(2026, 3, 1, 10, 30, 0), result.LocalEnd);
        Assert.Equal("Europe/Istanbul", result.TimeZoneId);
        Assert.Null(result.StartDate);
        Assert.Null(result.EndDateExclusive);
        Assert.Equal("Anatomy", result.Summary);
        Assert.Equal("Amfi 1", result.Location);
    }

    [Fact]
    public void TheoryPreservesItsSequenceNumberAndLabelsDescriptionFields()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(
            displayTitle: "5-Biyofiziğe giriş-temel kavramlar",
            instructor: "Prof. Dr. Muhammet BEKTAŞ",
            curriculumBlock: "YAŞAMIN MOLEKÜLER TEMELLERİ",
            departments: ["BİYOFİZİK AD."]);

        ManagedCalendarEvent result = ManagedCalendarEventFactory.ToManagedEvent(UserId, record);

        Assert.Equal("5-Biyofiziğe giriş-temel kavramlar", result.Summary);
        Assert.Equal(
            "Öğretim üyesi: Prof. Dr. Muhammet BEKTAŞ\n"
            + "Dilim: YAŞAMIN MOLEKÜLER TEMELLERİ\n"
            + "Anabilim dalı: BİYOFİZİK AD.",
            result.Description);
    }

    [Fact]
    public void AmphitheatreProgramInstructionIsNeverAnEventLocation()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(
            location: "FAKÜLTEMİZ WEB SİTESİ ÖĞRENCİ AĞI AMFİ PROGRAMINA BAKINIZ");

        ManagedCalendarEvent result = ManagedCalendarEventFactory.ToManagedEvent(UserId, record);

        Assert.Null(result.Location);
    }

    [Theory]
    [InlineData("ANATOMİ AD.", "#D50000")]
    [InlineData("FİZYOLOJİ AD.", "#1A237E")]
    [InlineData("TIBBİ BİYOKİMYA AD.", "#F4511E")]
    [InlineData("TIBBİ BİYOLOJİ AD.", "#0B8043")]
    [InlineData("HİSTOLOJİ VE EMBRİYOLOJİ AD.", "#8E24AA")]
    public void RequestedDepartmentsUseTheirRequestedColors(
        string department,
        string color)
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(departments: [department]);

        ManagedCalendarEvent result = ManagedCalendarEventFactory.ToManagedEvent(UserId, record);

        Assert.Equal(color, result.Label.BackgroundColor);
        Assert.True(Guid.TryParse(result.Label.Id, out _));
    }

    [Theory]
    [InlineData("PHYSIOLOGY DEPARTMENT", "#1A237E")]
    [InlineData("Biyokimya AD.", "#F4511E")]
    [InlineData("KBB", null)]
    public void DepartmentAliasesResolveToTheCanonicalCatalog(
        string department,
        string? expectedColor)
    {
        ManagedCalendarEvent result = ManagedCalendarEventFactory.ToManagedEvent(
            UserId,
            CalendarTestData.Record(departments: [department]));

        Assert.Contains("AD", result.Label.Name, StringComparison.Ordinal);
        if (expectedColor is not null)
        {
            Assert.Equal(expectedColor, result.Label.BackgroundColor);
        }
    }

    [Fact]
    public void AUserPaletteOverridesTheDepartmentDefaultWithoutChangingItsLabelIdentity()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(departments: ["ANATOMİ AD."]);
        ManagedCalendarEvent defaultEvent =
            ManagedCalendarEventFactory.ToManagedEvent(UserId, record);
        ManagedCalendarEvent customized = ManagedCalendarEventFactory.ToManagedEvent(
            UserId,
            record,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["anatomi"] = "#123456",
            });

        Assert.Equal(defaultEvent.Label.Id, customized.Label.Id);
        Assert.Equal("#123456", customized.Label.BackgroundColor);
    }

    [Fact]
    public void OfficialCatalogContainsAllFortyFiveFacultyDepartments()
    {
        Assert.Equal(45, DepartmentCatalog.Departments.Count);
        Assert.Equal(10, DepartmentCatalog.Departments.Count(item => item.Division == DepartmentDivision.Basic));
        Assert.Equal(21, DepartmentCatalog.Departments.Count(item => item.Division == DepartmentDivision.Internal));
        Assert.Equal(14, DepartmentCatalog.Departments.Count(item => item.Division == DepartmentDivision.Surgical));
        Assert.Equal(45, DepartmentCatalog.Departments.Select(item => item.Key).Distinct().Count());
    }

    [Fact]
    public void OtherDepartmentsReceiveStableDistinctCustomColors()
    {
        ManagedCalendarEvent biophysics = ManagedCalendarEventFactory.ToManagedEvent(
            UserId,
            CalendarTestData.Record(departments: ["BİYOFİZİK AD."]));
        ManagedCalendarEvent microbiology = ManagedCalendarEventFactory.ToManagedEvent(
            UserId,
            CalendarTestData.Record(departments: ["TIBBİ MİKROBİYOLOJİ AD."]));

        Assert.NotEqual(biophysics.Label.Id, microbiology.Label.Id);
        Assert.NotEqual(
            biophysics.Label.BackgroundColor,
            microbiology.Label.BackgroundColor);
    }

    [Fact]
    public void APracticeTitleUsesTheSameLabelAsItsDepartment()
    {
        ManagedCalendarEvent annual = ManagedCalendarEventFactory.ToManagedEvent(
            UserId,
            CalendarTestData.Record(departments: ["BİYOFİZİK AD."]));
        ManagedCalendarEvent practice = ManagedCalendarEventFactory.ToManagedEvent(
            UserId,
            CalendarTestData.Record(
                displayTitle: "Temel Biyofizik",
                eventType: ScheduleEventType.Practice));

        Assert.Equal(annual.Label, practice.Label);
    }

    [Theory]
    [InlineData(ScheduleEventType.Exam, "#616161")]
    [InlineData(ScheduleEventType.FreeStudy, "#039BE5")]
    public void SpecialEventTypesOverrideDepartmentColor(
        ScheduleEventType eventType,
        string color)
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(
            eventType: eventType,
            departments: ["ANATOMİ AD."]);

        ManagedCalendarEvent result = ManagedCalendarEventFactory.ToManagedEvent(UserId, record);

        Assert.Equal(color, result.Label.BackgroundColor);
    }

    [Fact]
    public void AnAllDayRecordBecomesAnAllDayEventWithAnExclusiveEnd()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(
            allDay: true,
            date: new DateOnly(2026, 10, 29));

        ManagedCalendarEvent result = ManagedCalendarEventFactory.ToManagedEvent(UserId, record);

        Assert.True(result.IsAllDay);
        Assert.Equal(new DateOnly(2026, 10, 29), result.StartDate);
        // Google treats the end date as exclusive, so a one-day closure ends the next day.
        Assert.Equal(new DateOnly(2026, 10, 30), result.EndDateExclusive);
        Assert.Null(result.LocalStart);
        Assert.Null(result.LocalEnd);
    }

    [Fact]
    public void ManagedEventsCarryTheIdentityAndContentTheyWereWrittenFrom()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "sha256:lesson");

        ManagedCalendarEvent result = ManagedCalendarEventFactory.ToManagedEvent(UserId, record);

        Assert.Equal("1", result.PrivateProperties[ManagedCalendarEventFactory.ManagedMarkerKey]);
        Assert.Equal("sha256:lesson", result.PrivateProperties["stableIdentity"]);
        Assert.Equal(record.ContentHash, result.PrivateProperties["contentHash"]);
        Assert.Equal(record.SourceId.Value, result.PrivateProperties["sourceId"]);
        Assert.Equal(record.Id.ToString(), result.PrivateProperties["canonicalRecordId"]);
    }

    [Fact]
    public void TheEventIdIsDeterministicForAUserAndLesson()
    {
        string first = ManagedCalendarEventFactory.DeterministicEventId(UserId, "sha256:lesson");
        string second = ManagedCalendarEventFactory.DeterministicEventId(UserId, "sha256:lesson");

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheEventIdDiffersByLessonAndByUser()
    {
        string lessonA = ManagedCalendarEventFactory.DeterministicEventId(UserId, "sha256:a");
        string lessonB = ManagedCalendarEventFactory.DeterministicEventId(UserId, "sha256:b");
        string otherUser = ManagedCalendarEventFactory.DeterministicEventId(
            Guid.CreateVersion7(),
            "sha256:a");

        Assert.NotEqual(lessonA, lessonB);
        Assert.NotEqual(lessonA, otherUser);
    }

    [Fact]
    public void TheEventIdIsAValidGoogleCalendarId()
    {
        string id = ManagedCalendarEventFactory.DeterministicEventId(UserId, "sha256:lesson");

        // Google requires 5-1024 characters from the base32hex alphabet (a-v and 0-9).
        Assert.InRange(id.Length, 5, 1024);
        Assert.All(id, character => Assert.Contains(character, Base32HexAlphabet));
    }
}
