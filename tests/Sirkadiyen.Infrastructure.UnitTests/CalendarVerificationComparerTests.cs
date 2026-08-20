using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class CalendarVerificationComparerTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static readonly DateTimeOffset CheckedAt = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, string> NoColors =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void AMatchingCalendarIsReportedInSync()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-1");
        ManagedCalendarEventSnapshot actual = ActualFor(record);

        CalendarVerificationReport report = Compare(
            expected: [record],
            ledger: [Mapping(record)],
            actual: [actual]);

        Assert.True(report.InSync);
        Assert.Equal(1, report.MatchedCount);
        Assert.Equal(0, report.MissingOnGoogleCount);
        Assert.Empty(report.Items);
    }

    [Fact]
    public void ALessonInTheLedgerButAbsentFromGoogleIsMissing()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-missing");

        CalendarVerificationReport report = Compare(
            expected: [record],
            ledger: [Mapping(record)],
            actual: []);

        Assert.False(report.InSync);
        Assert.Equal(1, report.MissingOnGoogleCount);
        CalendarVerificationDiff item = Assert.Single(report.Items);
        Assert.Equal(CalendarVerificationDiffKind.MissingOnGoogle, item.Kind);
        Assert.True(item.InLedger);
    }

    [Fact]
    public void AGoogleEventThatNoLongerAppliesIsExtra()
    {
        CanonicalScheduleRecord orphan = CalendarTestData.Record(stableIdentity: "lesson-orphan");
        ManagedCalendarEventSnapshot actual = ActualFor(orphan);

        // The record is not in the expected set, so its event should not be there.
        CalendarVerificationReport report = Compare(
            expected: [],
            ledger: [],
            actual: [actual]);

        Assert.False(report.InSync);
        Assert.Equal(1, report.ExtraOnGoogleCount);
        Assert.Equal(CalendarVerificationDiffKind.ExtraOnGoogle, Assert.Single(report.Items).Kind);
    }

    [Fact]
    public void AGoogleEventWithChangedContentIsDrift()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-drift");
        ManagedCalendarEventSnapshot actual = ActualFor(record) with { Summary = "Elle düzenlenmiş" };

        CalendarVerificationReport report = Compare(
            expected: [record],
            ledger: [Mapping(record)],
            actual: [actual]);

        Assert.False(report.InSync);
        Assert.Equal(1, report.ContentDriftCount);
        Assert.Equal(0, report.MatchedCount);
        Assert.Equal(CalendarVerificationDiffKind.ContentDrift, Assert.Single(report.Items).Kind);
    }

    [Fact]
    public void TwoGoogleEventsForOneLessonAreADuplicate()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-dup");
        ManagedCalendarEventSnapshot actual = ActualFor(record);

        CalendarVerificationReport report = Compare(
            expected: [record],
            ledger: [Mapping(record)],
            actual: [actual, actual]);

        Assert.False(report.InSync);
        Assert.Equal(1, report.DuplicateCount);
        Assert.Equal(CalendarVerificationDiffKind.Duplicate, Assert.Single(report.Items).Kind);
    }

    [Fact]
    public void AnAnnouncementEventIsIgnoredAndAnUnmarkedEventIsCounted()
    {
        CanonicalScheduleRecord record = CalendarTestData.Record(stableIdentity: "lesson-ok");
        ManagedCalendarEventSnapshot matching = ActualFor(record);
        ManagedCalendarEventSnapshot announcement = Marked(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ManagedCalendarEventFactory.ManagedMarkerKey] = "1",
            [ManagedCalendarEventFactory.KindKey] = ManagedCalendarEventFactory.AnnouncementKind,
        });
        ManagedCalendarEventSnapshot unmarked = Marked(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ManagedCalendarEventFactory.ManagedMarkerKey] = "1",
        });

        CalendarVerificationReport report = Compare(
            expected: [record],
            ledger: [Mapping(record)],
            actual: [matching, announcement, unmarked]);

        // The announcement is not schedule truth, so it is neither matched nor extra nor unmarked.
        Assert.Equal(1, report.MatchedCount);
        Assert.Equal(0, report.ExtraOnGoogleCount);
        Assert.Equal(1, report.UnmarkedCount);
        Assert.False(report.InSync);
    }

    private static CalendarVerificationReport Compare(
        IReadOnlyList<CanonicalScheduleRecord> expected,
        IReadOnlyList<CalendarEventMappingView> ledger,
        IReadOnlyList<ManagedCalendarEventSnapshot> actual) =>
        CalendarVerificationComparer.Compare(
            UserId,
            "cal-under-test",
            CheckedAt,
            expected,
            ledger,
            actual,
            NoColors,
            maxItems: 200);

    private static CalendarEventMappingView Mapping(CanonicalScheduleRecord record) => new()
    {
        UserId = UserId,
        StableIdentity = record.StableIdentity,
        SourceId = record.SourceId,
        GoogleCalendarId = "cal-under-test",
        GoogleEventId = ManagedCalendarEventFactory.DeterministicEventId(UserId, record.StableIdentity),
        ContentHash = record.ContentHash,
        CanonicalRecordId = record.Id,
    };

    private static ManagedCalendarEventSnapshot ActualFor(CanonicalScheduleRecord record)
    {
        ManagedCalendarEvent calendarEvent = ManagedCalendarEventFactory.ToManagedEvent(UserId, record, NoColors);
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(calendarEvent.TimeZoneId);
        return new ManagedCalendarEventSnapshot
        {
            EventId = calendarEvent.EventId,
            Summary = calendarEvent.Summary,
            Description = calendarEvent.Description,
            Location = calendarEvent.Location,
            EventLabelId = calendarEvent.Label.Id,
            IsAllDay = calendarEvent.IsAllDay,
            StartDate = calendarEvent.StartDate,
            EndDateExclusive = calendarEvent.EndDateExclusive,
            StartAt = calendarEvent.LocalStart is { } start
                ? new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), zone.GetUtcOffset(start))
                : null,
            EndAt = calendarEvent.LocalEnd is { } end
                ? new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Unspecified), zone.GetUtcOffset(end))
                : null,
            PrivateProperties = new Dictionary<string, string>(calendarEvent.PrivateProperties, StringComparer.Ordinal),
        };
    }

    private static ManagedCalendarEventSnapshot Marked(IReadOnlyDictionary<string, string> properties) => new()
    {
        EventId = $"evt-{Guid.NewGuid():N}",
        Summary = "Other",
        IsAllDay = false,
        StartAt = CheckedAt,
        EndAt = CheckedAt.AddHours(1),
        PrivateProperties = properties,
    };
}
