using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Announcements.Stores;
using Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;
using Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// The announcement aggregate and its delivery ledger against a real PostgreSQL database
/// (ADR-107): the deduplication index, the audience resolution with its exclusion reasons, and the
/// transitions delivery depends on.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AnnouncementStoreTests(PostgresFixture fixture)
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.app.created";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly EventDate = new(2026, 11, 12);

    /// <summary>How far a seeded account got towards being able to receive a calendar write.</summary>
    private enum Stage
    {
        NoConnection,
        InProgress,
        Completed,
        CompletedThenReauth,
    }

    [Fact]
    public async Task TheCampaignKeyIndexTurnsASecondConfirmationIntoAReplay()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid author = (await CreateUserAsync("ann-author")).UserId;
        Guid recipient = await SetUpAsync("ann-dup", Stage.Completed);
        string campaignKey = $"bulk:2026-11-12:{Guid.NewGuid():N}"[..40];

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AnnouncementStore store = new(context);

        AnnouncementCreateStoreResult first = await store.AddAsync(
            Announcement(campaignKey, author, out Guid firstId),
            [Pending(firstId, recipient)],
            Token);

        AnnouncementCreateStoreResult second = await store.AddAsync(
            Announcement(campaignKey, author, out Guid secondId),
            [Pending(secondId, recipient)],
            Token);

        // The unique index is the real guarantee, not the application's earlier lookup: two
        // operators confirming concurrently must not both win.
        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Announcement.AnnouncementId, second.Announcement.AnnouncementId);
    }

    [Fact]
    public async Task DeliveryCountersAreDerivedFromTheLedgerRatherThanStored()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid author = (await CreateUserAsync("ann-count-author")).UserId;
        Guid written = await SetUpAsync("ann-count-written", Stage.Completed);
        Guid skipped = await SetUpAsync("ann-count-skipped", Stage.InProgress);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AnnouncementStore store = new(context);

        CalendarAnnouncement announcement = Announcement(
            $"bulk:2026-11-12:{Guid.NewGuid():N}"[..40],
            author,
            out Guid announcementId);
        await store.AddAsync(
            announcement,
            [
                Pending(announcementId, written),
                CalendarAnnouncementDelivery.Excluded(
                    announcementId,
                    skipped,
                    AnnouncementExclusionReason.InitialSyncIncomplete,
                    Now),
            ],
            Token);

        IReadOnlyList<AnnouncementDeliveryTarget> targets = await store.ListDeliveryTargetsAsync(
            announcementId,
            CalendarAnnouncementDeliveryState.Pending,
            10,
            Token);
        AnnouncementDeliveryTarget target = Assert.Single(targets);
        await store.MarkDeliveryWrittenAsync(target.DeliveryId, "google-event-1", 1, Now, Token);

        AnnouncementDetail? detail = await store.FindAsync(announcementId, Token);

        Assert.NotNull(detail);
        Assert.Equal(1, detail.Summary.Counts.Written);
        Assert.Equal(0, detail.Summary.Counts.Pending);
        Assert.Equal(1, detail.Summary.Counts.Skipped);
        Assert.Equal(
            AnnouncementExclusionReason.InitialSyncIncomplete,
            Assert.Single(detail.Exclusions).Reason);
    }

    [Fact]
    public async Task ADeliveryTargetCarriesTheEligibilityAsItStandsNowNotAtConfirmation()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid author = (await CreateUserAsync("ann-stale-author")).UserId;
        Guid recipient = await SetUpAsync("ann-stale", Stage.Completed);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AnnouncementStore store = new(context);
        CalendarAnnouncement announcement = Announcement(
            $"bulk:2026-11-12:{Guid.NewGuid():N}"[..40],
            author,
            out Guid announcementId);
        await store.AddAsync(announcement, [Pending(announcementId, recipient)], Token);

        // The grant dies between confirmation and delivery.
        await using (SirkadiyenDbContext mutate = fixture.CreateProductionLikeContext())
        {
            await new GoogleCalendarConnectionStore(mutate)
                .MarkNeedsReauthorizationAsync(recipient, Now.AddHours(1), Token);
        }

        AnnouncementDeliveryTarget target = Assert.Single(
            await store.ListDeliveryTargetsAsync(
                announcementId,
                CalendarAnnouncementDeliveryState.Pending,
                10,
                Token));

        Assert.Equal(
            AnnouncementExclusionReason.CalendarAuthorizationRevoked,
            target.CurrentExclusion);
        // The credential is withheld when the recipient is ineligible: nothing should be able to
        // attempt a write with it.
        Assert.Null(target.ProtectedRefreshToken);
    }

    [Fact]
    public async Task AnEditReopensEveryWrittenCopyForAPatchInOneTransaction()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid author = (await CreateUserAsync("ann-edit-author")).UserId;
        Guid recipient = await SetUpAsync("ann-edit", Stage.Completed);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AnnouncementStore store = new(context);
        CalendarAnnouncement announcement = Announcement(
            $"bulk:2026-11-12:{Guid.NewGuid():N}"[..40],
            author,
            out Guid announcementId);
        await store.AddAsync(announcement, [Pending(announcementId, recipient)], Token);

        AnnouncementDeliveryTarget target = Assert.Single(
            await store.ListDeliveryTargetsAsync(
                announcementId,
                CalendarAnnouncementDeliveryState.Pending,
                10,
                Token));
        await store.MarkDeliveryWrittenAsync(target.DeliveryId, "google-event-2", 1, Now, Token);

        UpdateAnnouncementResult result = await store.UpdateContentAsync(
            announcementId,
            Content("Düzeltilmiş başlık"),
            "admin@example.test",
            "Saat yanlış yazılmıştı.",
            Now.AddHours(1),
            Token);

        Assert.Equal(UpdateAnnouncementOutcome.Updated, result.Outcome);
        Assert.Equal(2, result.Announcement!.ContentVersion);

        // The written copy is a version behind, so it must be queued again — otherwise the
        // recipient keeps text no pass would ever correct.
        AnnouncementDeliveryTarget reopened = Assert.Single(
            await store.ListDeliveryTargetsAsync(
                announcementId,
                CalendarAnnouncementDeliveryState.Pending,
                10,
                Token));
        Assert.Equal(1, reopened.AppliedContentVersion);
        Assert.Equal("google-event-2", reopened.GoogleEventId);
    }

    [Fact]
    public async Task CancellingSkipsUndeliveredRecipientsAndQueuesTheWrittenOnesForRemoval()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid author = (await CreateUserAsync("ann-cancel-author")).UserId;
        Guid delivered = await SetUpAsync("ann-cancel-written", Stage.Completed);
        Guid undelivered = await SetUpAsync("ann-cancel-pending", Stage.Completed);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AnnouncementStore store = new(context);
        CalendarAnnouncement announcement = Announcement(
            $"bulk:2026-11-12:{Guid.NewGuid():N}"[..40],
            author,
            out Guid announcementId);
        await store.AddAsync(
            announcement,
            [Pending(announcementId, delivered), Pending(announcementId, undelivered)],
            Token);

        AnnouncementDeliveryTarget first = (await store.ListDeliveryTargetsAsync(
            announcementId,
            CalendarAnnouncementDeliveryState.Pending,
            10,
            Token))[0];
        await store.MarkDeliveryWrittenAsync(first.DeliveryId, "google-event-3", 1, Now, Token);

        CancelAnnouncementResult result = await store.RequestCancellationAsync(
            announcementId,
            "admin@example.test",
            "Etkinlik ertelendi.",
            Now.AddHours(1),
            Token);

        Assert.Equal(CancelAnnouncementOutcome.CancellationRequested, result.Outcome);

        // Nobody new receives it, and the copy that exists is queued for removal rather than
        // silently forgotten.
        Assert.Empty(await store.ListDeliveryTargetsAsync(
            announcementId,
            CalendarAnnouncementDeliveryState.Pending,
            10,
            Token));
        Assert.Single(await store.ListDeliveryTargetsAsync(
            announcementId,
            CalendarAnnouncementDeliveryState.Written,
            10,
            Token));

        PagedResult<AnnouncementDeliveryView> ledger =
            await store.ListDeliveriesAsync(announcementId, null, 1, 50, Token);
        Assert.Equal(2, ledger.TotalCount);
    }

    [Fact]
    public async Task ADeferredAnnouncementIsNotDispatchableUntilItsBackOffElapses()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid author = (await CreateUserAsync("ann-defer-author")).UserId;
        Guid recipient = await SetUpAsync("ann-defer", Stage.Completed);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AnnouncementStore store = new(context);
        CalendarAnnouncement announcement = Announcement(
            $"bulk:2026-11-12:{Guid.NewGuid():N}"[..40],
            author,
            out Guid announcementId);
        await store.AddAsync(announcement, [Pending(announcementId, recipient)], Token);

        await store.DeferAfterFailureAsync(
            announcementId,
            "Google kota sınırı.",
            Now.AddMinutes(30),
            Now,
            Token);

        Assert.DoesNotContain(
            await store.ListDispatchableAsync(Now.AddMinutes(5), 100, Token),
            candidate => candidate.AnnouncementId == announcementId);
        Assert.Contains(
            await store.ListDispatchableAsync(Now.AddMinutes(31), 100, Token),
            candidate => candidate.AnnouncementId == announcementId);
    }

    [Fact]
    public async Task TheAudienceResolutionReportsEveryCandidateWithTheReasonTheyCannotReceiveIt()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid eligible = await SetUpAsync("aud-eligible", Stage.Completed, selector: "A");
        Guid otherGroup = await SetUpAsync("aud-othergroup", Stage.Completed, selector: "B");
        Guid noConnection = await SetUpAsync("aud-noconn", Stage.NoConnection, selector: "A");
        Guid incomplete = await SetUpAsync("aud-incomplete", Stage.InProgress, selector: "A");
        Guid revokedGrant = await SetUpAsync("aud-reauth", Stage.CompletedThenReauth, selector: "A");
        Guid unlicensed = await SetUpAsync(
            "aud-unlicensed",
            Stage.Completed,
            selector: "A",
            licensed: false);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        AnnouncementAudienceResolution resolution =
            await new AnnouncementAudienceReadStore(context).ResolveAsync(
                new AnnouncementAudienceCriteria
                {
                    AcademicYear = "2025-2026",
                    ClassYear = 1,
                    ProgramLanguage = ProgramLanguage.Turkish,
                    Selectors = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["practiceGroup"] = "A",
                    },
                },
                Token);

        Assert.Contains(resolution.Included, candidate => candidate.UserId == eligible);

        // A student in another practice group was never addressed, so they are absent entirely
        // rather than listed as an exclusion: nothing about their account is wrong.
        Assert.DoesNotContain(resolution.Included, candidate => candidate.UserId == otherGroup);
        Assert.DoesNotContain(resolution.Excluded, candidate => candidate.UserId == otherGroup);

        Assert.Equal(
            AnnouncementExclusionReason.NoCalendarConnection,
            Reason(resolution, noConnection));
        Assert.Equal(
            AnnouncementExclusionReason.InitialSyncIncomplete,
            Reason(resolution, incomplete));
        Assert.Equal(
            AnnouncementExclusionReason.CalendarAuthorizationRevoked,
            Reason(resolution, revokedGrant));
        Assert.Equal(
            AnnouncementExclusionReason.LicenseInactive,
            Reason(resolution, unlicensed));
    }

    [Fact]
    public async Task ASingleUserResolutionIgnoresEveryCohortCriterion()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid target = await SetUpAsync("aud-single", Stage.Completed, selector: "B");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        AnnouncementAudienceResolution resolution =
            await new AnnouncementAudienceReadStore(context).ResolveAsync(
                new AnnouncementAudienceCriteria
                {
                    AcademicYear = "1999-2000",
                    ClassYear = 6,
                    ProgramLanguage = ProgramLanguage.English,
                    Selectors = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["practiceGroup"] = "Z",
                    },
                    TargetUserId = target,
                },
                Token);

        // A warning is addressed to a person, not to a cohort, so the cohort criteria are not a
        // filter it has to satisfy.
        Assert.Equal(target, Assert.Single(resolution.Included).UserId);
        Assert.Empty(resolution.Excluded);
    }

    [Fact]
    public async Task AnAccountWithNoAcademicProfileIsExcludedFromAWarningRatherThanRefused()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        UserSession user = await CreateUserAsync("aud-noprofile");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        AnnouncementAudienceResolution resolution =
            await new AnnouncementAudienceReadStore(context).ResolveAsync(
                new AnnouncementAudienceCriteria
                {
                    AcademicYear = "2025-2026",
                    TargetUserId = user.UserId,
                },
                Token);

        Assert.Empty(resolution.Included);
        Assert.Equal(
            AnnouncementExclusionReason.NoStudentProfile,
            Assert.Single(resolution.Excluded).ExclusionReason);
    }

    private static AnnouncementExclusionReason? Reason(
        AnnouncementAudienceResolution resolution,
        Guid userId) =>
        Assert.Single(resolution.Excluded, candidate => candidate.UserId == userId)
            .ExclusionReason;

    private static CalendarAnnouncement Announcement(
        string campaignKey,
        Guid author,
        out Guid announcementId)
    {
        CalendarAnnouncement announcement = CalendarAnnouncement.Create(
            CalendarAnnouncementKind.Bulk,
            campaignKey,
            null,
            Content("Telafi dersi"),
            new AnnouncementAudienceDefinition
            {
                AcademicYear = "2025-2026",
                ClassYear = 1,
                ProgramLanguage = ProgramLanguage.Turkish,
                Selectors = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["practiceGroup"] = "A",
                },
            },
            "plan-hash",
            1,
            0,
            author,
            "admin@example.test",
            "Fakülte istedi.",
            Now);
        announcementId = announcement.Id;
        return announcement;
    }

    private static AnnouncementContent Content(string title) => new()
    {
        Title = title,
        Body = "Telafi dersi yapılacaktır.",
        IsAllDay = false,
        LocalDate = EventDate,
        StartLocalTime = new TimeOnly(9, 0),
        EndLocalTime = new TimeOnly(10, 0),
        TimeZoneId = AnnouncementService.TimeZoneId,
        CategoryKey = AnnouncementCategoryCatalog.DefaultKey,
    };

    private static CalendarAnnouncementDelivery Pending(Guid announcementId, Guid userId) =>
        CalendarAnnouncementDelivery.Pending(announcementId, userId, $"cal-{userId:N}", Now);

    private async Task<Guid> SetUpAsync(
        string prefix,
        Stage stage,
        string selector = "A",
        bool licensed = true)
    {
        UserSession user = await CreateUserAsync(prefix);

        if (licensed)
        {
            UserSession admin = await CreateUserAsync($"{prefix}-admin");
            await using SirkadiyenDbContext licenseContext = fixture.CreateProductionLikeContext();
            await new LicenseStore(licenseContext).ActivateManuallyAsync(
                user.UserId,
                admin.UserId,
                admin.Email,
                "Seeded by the test.",
                Now,
                Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await new StudentProfileStore(context).UpsertAsync(
            user.UserId,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "0101240048",
            "1.0",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["practiceGroup"] = selector },
            Now,
            Token);

        if (stage is Stage.NoConnection)
        {
            return user.UserId;
        }

        GoogleCalendarConnectionStore connections = new(context);
        await connections.UpsertAuthorizationAsync(
            user.UserId,
            $"protected:tok-{prefix}",
            Scope,
            Now,
            Token);
        await connections.RequestInitialSyncAsync(user.UserId, Now.AddMinutes(1), Token);

        if (stage is Stage.Completed or Stage.CompletedThenReauth)
        {
            await connections.AttachManagedCalendarAsync(
                user.UserId,
                $"cal-{user.UserId:N}",
                Now.AddMinutes(2),
                Token);
            await connections.MarkInitialSyncCompletedAsync(user.UserId, Now.AddMinutes(3), Token);
        }

        if (stage is Stage.CompletedThenReauth)
        {
            await connections.MarkNeedsReauthorizationAsync(user.UserId, Now.AddMinutes(4), Token);
        }

        return user.UserId;
    }

    private async Task<UserSession> CreateUserAsync(string prefix)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        string nonce = Guid.NewGuid().ToString("N");
        return await new UserStore(context).SignInWithGoogleAsync(
            new GoogleIdentity
            {
                Subject = $"{prefix}-{nonce}",
                Email = $"{prefix}-{nonce}@example.com",
                EmailVerified = true,
            },
            UserRole.User,
            Now,
            Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
