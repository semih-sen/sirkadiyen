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
