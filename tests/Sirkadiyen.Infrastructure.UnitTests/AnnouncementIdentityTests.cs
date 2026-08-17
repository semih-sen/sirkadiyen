using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The three deterministic derivations an announcement depends on for not duplicating itself
/// (ADR-107): the campaign key, the plan hash, and the calendar event id.
/// </summary>
public sealed class AnnouncementIdentityTests
{
    private static readonly DateOnly Date = new(2026, 11, 12);

    [Fact]
    public void TheSameBulkAnnouncementAlwaysDerivesTheSameCampaignKey()
    {
        string first = BulkKey(Selectors(("practiceGroup", "A"), ("anatomyGroup", "2")));
        string second = BulkKey(Selectors(("anatomyGroup", "2"), ("practiceGroup", "A")));

        // Selector order is the operator filling a form, not a property of the announcement.
        Assert.Equal(first, second);
        Assert.StartsWith("bulk:2026-11-12:", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleRetypedWithDifferentCasingOrSpacingIsTheSameCampaign()
    {
        Assert.Equal(
            BulkKey(Selectors(), title: "Telafi Dersi"),
            BulkKey(Selectors(), title: "  telafi   dersi "));
    }

    [Fact]
    public void ADifferentAudienceIsADifferentCampaign()
    {
        Assert.NotEqual(
            BulkKey(Selectors(("practiceGroup", "A"))),
            BulkKey(Selectors(("practiceGroup", "B"))));
    }

    [Fact]
    public void AWarningKeyIsTheUserTheTemplateAndTheDay()
    {
        Guid userId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal(
            $"warning:{userId:N}:profile-missing:2026-11-12",
            AnnouncementCampaignKey.ForUserWarning(userId, "profile-missing", Date));
    }

    [Fact]
    public void ASameSizedAudienceWithDifferentPeopleProducesADifferentPlanHash()
    {
        AnnouncementContent content = Content();
        AnnouncementAudienceCriteria criteria = Criteria();

        string first = AnnouncementPlanHasher.Compute(
            "bulk:key",
            content,
            criteria,
            Resolution(Guid.Parse("00000000-0000-0000-0000-000000000001")));
        string second = AnnouncementPlanHasher.Compute(
            "bulk:key",
            content,
            criteria,
            Resolution(Guid.Parse("00000000-0000-0000-0000-000000000002")));

        // Confirming "1 recipient" must not authorize writing to a different recipient, which is
        // the whole reason the identities are hashed rather than the count.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AnAnnouncementEventIdIsStablePerUserAndDisjointFromLessonIds()
    {
        Guid userId = Guid.CreateVersion7();
        Guid announcementId = Guid.CreateVersion7();

        string once = AnnouncementEventFactory.DeterministicEventId(userId, announcementId);
        string twice = AnnouncementEventFactory.DeterministicEventId(userId, announcementId);

        Assert.Equal(once, twice);
        Assert.NotEqual(
            once,
            ManagedCalendarEventFactory.DeterministicEventId(userId, announcementId.ToString("N")));
        Assert.NotEqual(
            once,
            AnnouncementEventFactory.DeterministicEventId(Guid.CreateVersion7(), announcementId));
    }

    [Fact]
    public void AnAnnouncementEventCarriesTheKindMarkerInventoryLooksFor()
    {
        ManagedCalendarEvent calendarEvent = AnnouncementEventFactory.ToManagedEvent(
            Guid.CreateVersion7(),
            Candidate());

        Assert.Equal(
            ManagedCalendarEventFactory.AnnouncementKind,
            calendarEvent.PrivateProperties[ManagedCalendarEventFactory.KindKey]);
        Assert.Equal("1", calendarEvent.PrivateProperties[ManagedCalendarEventFactory.ManagedMarkerKey]);

        // No stable identity: an announcement is not a lesson, and claiming one would put it into
        // the identity space the schedule ledger owns.
        Assert.False(calendarEvent.PrivateProperties.ContainsKey("stableIdentity"));
    }

    [Fact]
    public void ATimedAnnouncementCarriesLocalTimesAndItsReminder()
    {
        ManagedCalendarEvent calendarEvent = AnnouncementEventFactory.ToManagedEvent(
            Guid.CreateVersion7(),
            Candidate() with { ReminderMinutesBefore = 30 });

        Assert.False(calendarEvent.IsAllDay);
        Assert.Equal(new DateTime(2026, 11, 12, 9, 0, 0), calendarEvent.LocalStart);
        Assert.Equal(new DateTime(2026, 11, 12, 9, 30, 0), calendarEvent.LocalEnd);
        Assert.Null(calendarEvent.StartDate);
        Assert.Equal(30, calendarEvent.ReminderMinutesBefore);
    }

    [Fact]
    public void AnAllDayAnnouncementEndsOnTheFollowingDayAndCarriesNoTimes()
    {
        ManagedCalendarEvent calendarEvent = AnnouncementEventFactory.ToManagedEvent(
            Guid.CreateVersion7(),
            Candidate() with
            {
                IsAllDay = true,
                StartLocalTime = null,
                EndLocalTime = null,
            });

        Assert.True(calendarEvent.IsAllDay);
        Assert.Equal(Date, calendarEvent.StartDate);
        Assert.Equal(Date.AddDays(1), calendarEvent.EndDateExclusive);
        Assert.Null(calendarEvent.LocalStart);
        Assert.Null(calendarEvent.LocalEnd);
    }

    [Fact]
    public void AnUnknownCategoryIsRefusedRatherThanGivenAnAccidentalColour()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnnouncementEventFactory.ToManagedEvent(
                Guid.CreateVersion7(),
                Candidate() with { CategoryKey = "announcement:invented" }));
    }

    private static string BulkKey(
        IReadOnlyDictionary<string, string> selectors,
        string title = "Telafi Dersi") =>
        AnnouncementCampaignKey.ForBulk("2025-2026", 2, "Turkish", selectors, Date, title);

    private static Dictionary<string, string> Selectors(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static AnnouncementContent Content() => new()
    {
        Title = "Telafi",
        Body = "Gövde",
        IsAllDay = false,
        LocalDate = Date,
        StartLocalTime = new TimeOnly(9, 0),
        EndLocalTime = new TimeOnly(9, 30),
        TimeZoneId = AnnouncementService.TimeZoneId,
        CategoryKey = AnnouncementCategoryCatalog.DefaultKey,
    };

    private static AnnouncementAudienceCriteria Criteria() => new()
    {
        AcademicYear = "2025-2026",
        ClassYear = 2,
        ProgramLanguage = ProgramLanguage.Turkish,
    };

    private static AnnouncementAudienceResolution Resolution(Guid userId) => new()
    {
        Included =
        [
            new AnnouncementAudienceCandidate
            {
                UserId = userId,
                Email = "a@example.test",
                ManagedCalendarId = "cal",
            },
        ],
        Excluded = [],
    };

    private static AnnouncementDispatchCandidate Candidate() => new()
    {
        AnnouncementId = Guid.CreateVersion7(),
        Kind = CalendarAnnouncementKind.Bulk,
        Status = CalendarAnnouncementStatus.Queued,
        ContentVersion = 1,
        DeliveryAttempts = 0,
        Title = "Telafi",
        Body = "Gövde",
        IsAllDay = false,
        LocalDate = Date,
        StartLocalTime = new TimeOnly(9, 0),
        EndLocalTime = new TimeOnly(9, 30),
        TimeZoneId = AnnouncementService.TimeZoneId,
        CategoryKey = AnnouncementCategoryCatalog.DefaultKey,
    };
}

/// <summary>
/// The audience rule is the opposite of the lesson one: every named selector must match, because
/// the operator is narrowing who they address rather than listing groups a lesson is for.
/// </summary>
public sealed class AnnouncementAudienceMatcherTests
{
    [Fact]
    public void EverySelectorMustMatchNotJustOne()
    {
        Dictionary<string, string> audience = new(StringComparer.Ordinal)
        {
            ["practiceGroup"] = "A",
            ["anatomyGroup"] = "2",
        };

        Assert.True(AnnouncementAudienceMatcher.Matches(
            audience,
            Profile(("practiceGroup", "A"), ("anatomyGroup", "2"))));
        Assert.False(AnnouncementAudienceMatcher.Matches(
            audience,
            Profile(("practiceGroup", "A"), ("anatomyGroup", "3"))));
    }

    [Fact]
    public void ADimensionTheProfileDoesNotCarryIsAMismatch()
    {
        // Treating an absent dimension as "matches anything" would send a message meant for one
        // anatomy group to every student whose programme has no anatomy group at all.
        Assert.False(AnnouncementAudienceMatcher.Matches(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["anatomyGroup"] = "2" },
            Profile(("practiceGroup", "A"))));
    }

    [Fact]
    public void AnEmptyAudienceMatchesEveryProfileInTheCohort()
    {
        Assert.True(AnnouncementAudienceMatcher.Matches(
            new Dictionary<string, string>(StringComparer.Ordinal),
            Profile(("practiceGroup", "A"))));
    }

    private static Dictionary<string, string> Profile(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
