using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Meals;
using Sirkadiyen.Domain.Meals;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>The pure, deterministic pieces the cafeteria menu depends on (ADR-150).</summary>
public sealed class MealMenuTextTests
{
    [Fact]
    public void NormalizeTurnsTheApiCommaCrlfListIntoOneDishPerLine()
    {
        string normalized = MealMenuText.Normalize(
            "Ezogelin Çorbası,\r\nKuru Köfte (Patates Garnili),\r\nSoslu Şakşuka,\r\nKıbrıs Tatlısı");

        Assert.Equal(
            "Ezogelin Çorbası\nKuru Köfte (Patates Garnili)\nSoslu Şakşuka\nKıbrıs Tatlısı",
            normalized);
    }

    [Fact]
    public void NormalizeDropsBlankLinesAndTrailingSeparators()
    {
        Assert.Equal("Çorba\nKöfte", MealMenuText.Normalize("  Çorba, \r\n\r\n Köfte, \r\n"));
    }

    [Fact]
    public void TheHashChangesExactlyWhenTheNormalizedTextDoes()
    {
        string a = MealMenuText.Hash(MealMenuText.Normalize("Çorba,\r\nKöfte"));
        string sameContentDifferentWhitespace =
            MealMenuText.Hash(MealMenuText.Normalize("Çorba,\r\n Köfte "));
        string different = MealMenuText.Hash(MealMenuText.Normalize("Çorba,\r\nPilav"));

        Assert.Equal(a, sameContentDifferentWhitespace);
        Assert.NotEqual(a, different);
    }
}

/// <summary>The event id and shape a menu is written under (ADR-150).</summary>
public sealed class MealEventFactoryTests
{
    private static readonly DateOnly Date = new(2026, 9, 6);

    private static readonly MealEventPresentation Presentation = new()
    {
        StartLocalTime = new TimeOnly(12, 30),
        EndLocalTime = new TimeOnly(13, 0),
        TimeZoneId = "Europe/Istanbul",
    };

    [Fact]
    public void TheIdentityIsTheMealAndTheDate()
    {
        Assert.Equal("meal:lunch:2026-09-06", MealEventFactory.EventIdentity(Date, MealCategory.Lunch));
    }

    [Fact]
    public void TheEventIdIsStablePerUserAndDateAndDisjointFromOtherKinds()
    {
        Guid user = Guid.CreateVersion7();

        string once = MealEventFactory.DeterministicEventId(user, Date, MealCategory.Lunch);
        string twice = MealEventFactory.DeterministicEventId(user, Date, MealCategory.Lunch);

        Assert.Equal(once, twice);

        // A different user, a different day, and a different meal are each a different event.
        Assert.NotEqual(once, MealEventFactory.DeterministicEventId(Guid.CreateVersion7(), Date, MealCategory.Lunch));
        Assert.NotEqual(once, MealEventFactory.DeterministicEventId(user, Date.AddDays(1), MealCategory.Lunch));
        Assert.NotEqual(once, MealEventFactory.DeterministicEventId(user, Date, MealCategory.Dinner));

        // And disjoint from a lesson's identity space: a lesson id derives from a hex stable
        // identity, which no "meal:"-prefixed identity can spell.
        Assert.NotEqual(
            once,
            ManagedCalendarEventFactory.DeterministicEventId(user, "deadbeefdeadbeefdeadbeefdeadbeef"));
    }

    [Fact]
    public void ALunchIsTimedBetweenTheConfiguredHoursAndCarriesTheMealMarker()
    {
        ManagedCalendarEvent calendarEvent = MealEventFactory.ToManagedEvent(
            Guid.CreateVersion7(),
            new MealMenuDayContent
            {
                LocalDate = Date,
                Category = MealCategory.Lunch,
                MealText = "Çorba\nKöfte",
                ContentVersion = 3,
            },
            Presentation);

        Assert.False(calendarEvent.IsAllDay);
        Assert.Equal(new DateTime(2026, 9, 6, 12, 30, 0), calendarEvent.LocalStart);
        Assert.Equal(new DateTime(2026, 9, 6, 13, 0, 0), calendarEvent.LocalEnd);
        Assert.Equal("Çorba\nKöfte", calendarEvent.Description);
        Assert.Equal(
            ManagedCalendarEventFactory.MealKind,
            calendarEvent.PrivateProperties[ManagedCalendarEventFactory.KindKey]);
        Assert.Equal("3", calendarEvent.PrivateProperties["contentVersion"]);

        // Not a lesson: it must never claim the schedule ledger's identity key.
        Assert.False(calendarEvent.PrivateProperties.ContainsKey("stableIdentity"));

        // Inventory and verification leave it alone because of the marker.
        Assert.True(ManagedCalendarEventFactory.IsNonScheduleKind(
            calendarEvent.PrivateProperties[ManagedCalendarEventFactory.KindKey]));
    }
}

/// <summary>The menu-day change-detection and conservative-withdrawal rules (ADR-150).</summary>
public sealed class MealMenuDayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 9, 6);

    [Fact]
    public void UnchangedContentDoesNotMoveTheVersionButRefreshesConfirmation()
    {
        MealMenuDay day = MealMenuDay.CreatePublished(Date, MealCategory.Lunch, "Çorba", "hash-a", Now);

        bool changed = day.ApplyObservedContent("Çorba", "hash-a", Now.AddHours(12));

        Assert.False(changed);
        Assert.Equal(1, day.ContentVersion);
        Assert.Equal(Now.AddHours(12), day.LastConfirmedAtUtc);
    }

    [Fact]
    public void ChangedContentBumpsTheVersion()
    {
        MealMenuDay day = MealMenuDay.CreatePublished(Date, MealCategory.Lunch, "Çorba", "hash-a", Now);

        bool changed = day.ApplyObservedContent("Pilav", "hash-b", Now.AddHours(12));

        Assert.True(changed);
        Assert.Equal(2, day.ContentVersion);
        Assert.Equal("Pilav", day.MealText);
    }

    [Fact]
    public void ASingleMissDoesNotWithdrawButTheThresholdDoes()
    {
        MealMenuDay day = MealMenuDay.CreatePublished(Date, MealCategory.Lunch, "Çorba", "hash-a", Now);

        Assert.False(day.RecordMiss(withdrawalThreshold: 3, Now.AddHours(1)));
        Assert.False(day.RecordMiss(withdrawalThreshold: 3, Now.AddHours(2)));
        Assert.Equal(MealMenuDayStatus.Published, day.Status);

        Assert.True(day.RecordMiss(withdrawalThreshold: 3, Now.AddHours(3)));
        Assert.Equal(MealMenuDayStatus.Withdrawn, day.Status);
    }

    [Fact]
    public void AConfirmationResetsAnAccumulatingMissAndRepublishesAWithdrawnDay()
    {
        MealMenuDay day = MealMenuDay.CreatePublished(Date, MealCategory.Lunch, "Çorba", "hash-a", Now);
        day.RecordMiss(withdrawalThreshold: 1, Now.AddHours(1));
        Assert.Equal(MealMenuDayStatus.Withdrawn, day.Status);

        bool changed = day.ApplyObservedContent("Çorba", "hash-a", Now.AddHours(2));

        // Same text returning is a republish, not a content change: the version must not move, or
        // every written copy would be needlessly patched.
        Assert.True(changed);
        Assert.Equal(MealMenuDayStatus.Published, day.Status);
        Assert.Equal(1, day.ContentVersion);
        Assert.Equal(0, day.ConsecutiveMissCount);
    }
}

/// <summary>The options guard rails (ADR-150).</summary>
public sealed class MealMenuOptionsTests
{
    [Fact]
    public void TheDefaultsAreValid() => new MealMenuOptions().Validate();

    [Fact]
    public void AnEventThatEndsBeforeItStartsIsRefused()
    {
        MealMenuOptions options = new()
        {
            StartLocalTime = new TimeOnly(13, 0),
            EndLocalTime = new TimeOnly(12, 30),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
