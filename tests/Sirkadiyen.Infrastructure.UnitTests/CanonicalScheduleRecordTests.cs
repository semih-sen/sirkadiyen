using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Pins the timed/all-day shape of a canonical record (ADR-046).
/// </summary>
/// <remarks>
/// The shape decides what reaches Google Calendar. A record that states one time
/// and not the other would become an event with no end, and one that claims to be
/// all-day while carrying times would be written twice over, so the invariant is
/// enforced in the constructor rather than trusted to each producer.
/// </remarks>
public sealed class CanonicalScheduleRecordTests
{
    [Fact]
    public void ATimedRecordStatesBothTimes()
    {
        CanonicalScheduleRecord record = Record(
            start: new TimeOnly(9, 0),
            end: new TimeOnly(9, 45),
            isAllDay: false);

        Assert.False(record.IsAllDay);
        Assert.Equal(new TimeOnly(9, 0), record.StartLocalTime);
        Assert.Equal(new TimeOnly(9, 45), record.EndLocalTime);
    }

    [Fact]
    public void AnAllDayRecordStatesNoTimeAtAll()
    {
        CanonicalScheduleRecord record = Record(start: null, end: null, isAllDay: true);

        Assert.True(record.IsAllDay);
        Assert.Null(record.StartLocalTime);
        Assert.Null(record.EndLocalTime);
        // One local date, because the sources write one row per closed day. The
        // exclusive end date Google wants is the calendar adapter's conversion.
        Assert.Equal(new DateOnly(2025, 10, 1), record.LocalDate);
    }

    [Theory]
    [InlineData(9, null)]
    [InlineData(null, 9)]
    [InlineData(null, null)]
    public void ATimedRecordMayNotBeMissingATime(int? startHour, int? endHour)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Record(
            start: startHour is { } start ? new TimeOnly(start, 0) : null,
            end: endHour is { } end ? new TimeOnly(end, 45) : null,
            isAllDay: false));

        Assert.Contains("both local times", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(9, null)]
    [InlineData(null, 9)]
    [InlineData(9, 10)]
    public void AnAllDayRecordMayNotCarryATime(int? startHour, int? endHour)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Record(
            start: startHour is { } start ? new TimeOnly(start, 0) : null,
            end: endHour is { } end ? new TimeOnly(end, 0) : null,
            isAllDay: true));

        Assert.Contains("no local time", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimedRecordStillMayNotEndBeforeItStarts()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Record(
            start: new TimeOnly(10, 0),
            end: new TimeOnly(9, 0),
            isAllDay: false));

        Assert.Contains("end after it starts", error.Message, StringComparison.Ordinal);
    }

    private static CanonicalScheduleRecord Record(
        TimeOnly? start,
        TimeOnly? end,
        bool isAllDay) => new(
            Guid.CreateVersion7(),
            SourceId.Parse("G1-TR-ANNUAL"),
            "1!R140",
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            isAllDay ? ScheduleEventType.Other : ScheduleEventType.Theory,
            AudienceScope.AllStudentsInProgram,
            "[]",
            "CUMHURİYET BAYRAMI",
            "cumhuriyet-bayrami",
            new DateOnly(2025, 10, 1),
            start,
            end,
            isAllDay,
            "Europe/Istanbul",
            "identity",
            "sha256:content",
            1.0m,
            "[]");
}
