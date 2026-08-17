using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The confirmation half of the six-step high-risk pattern (ADR-107): what the server computes,
/// what it binds a confirmation to, and what it refuses.
/// </summary>
public sealed class AnnouncementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly EventDate = new(2026, 11, 12);

    private static readonly Guid Actor = Guid.CreateVersion7();

    [Fact]
    public async Task ThePreviewReportsBothTheRecipientsAndTheExclusionsWithReasons()
    {
        Harness harness = new();
        harness.Audience.Included.Add(Candidate("a@example.test"));
        harness.Audience.Included.Add(Candidate("b@example.test"));
        harness.Audience.Excluded.Add(
            Candidate("c@example.test", AnnouncementExclusionReason.LicenseInactive));
        harness.Audience.Excluded.Add(
            Candidate("d@example.test", AnnouncementExclusionReason.LicenseInactive));
        harness.Audience.Excluded.Add(
            Candidate("e@example.test", AnnouncementExclusionReason.NoCalendarConnection));

        AnnouncementPreview preview =
            await harness.Build().PreviewAsync(Request(), CancellationToken.None);

        Assert.Equal(2, preview.RecipientCount);
        Assert.Equal(3, preview.ExcludedCount);
        Assert.Equal(
            [
                (AnnouncementExclusionReason.LicenseInactive, 2),
                (AnnouncementExclusionReason.NoCalendarConnection, 1),
            ],
            preview.Exclusions.Select(group => (group.Reason, group.Count)));

        // The count is what the operator has to type: it is the fact hardest to overlook.
        Assert.Equal("2", preview.ConfirmationPhrase);
        Assert.NotEmpty(preview.PlanHash);
        Assert.StartsWith("bulk:", preview.CampaignKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmingAPreviewQueuesTheAnnouncementAndFreezesEveryCandidateAsARow()
    {
        Harness harness = new();
        harness.Audience.Included.Add(Candidate("a@example.test"));
        harness.Audience.Excluded.Add(
            Candidate("b@example.test", AnnouncementExclusionReason.InitialSyncIncomplete));

        AnnouncementPreview preview =
            await harness.Build().PreviewAsync(Request(), CancellationToken.None);
        CreateAnnouncementResult result = await harness.Build().CreateAsync(
            Request(),
            preview.PlanHash,
            preview.ConfirmationPhrase,
            "Fakülte istedi.",
            Actor,
            "admin@example.test",
            CancellationToken.None);

        Assert.Equal(CreateAnnouncementOutcome.Queued, result.Outcome);
        Assert.Equal(CalendarAnnouncementStatus.Queued, harness.Store.Saved!.Status);
        Assert.Equal(1, harness.Store.Saved.RecipientCount);
        Assert.Equal(1, harness.Store.Saved.ExcludedCount);

        // The excluded candidate is recorded too: it is the evidence for the exclusion count the
        // operator approved, which would otherwise have nothing behind it a day later.
        Assert.Equal(2, harness.Store.SavedDeliveries.Count);
        Assert.Contains(
            harness.Store.SavedDeliveries,
            delivery => delivery.State is CalendarAnnouncementDeliveryState.Pending);
        CalendarAnnouncementDelivery skipped = Assert.Single(
            harness.Store.SavedDeliveries,
            delivery => delivery.State is CalendarAnnouncementDeliveryState.Skipped);
        Assert.Equal(AnnouncementExclusionReason.InitialSyncIncomplete, skipped.SkipReason);
    }

    [Fact]
    public async Task AnAudienceThatMovedSinceThePreviewIsRefusedRatherThanQueued()
    {
        Harness harness = new();
        harness.Audience.Included.Add(Candidate("a@example.test"));

        AnnouncementPreview preview =
            await harness.Build().PreviewAsync(Request(), CancellationToken.None);

        // A second student activates between the preview and the confirmation.
        harness.Audience.Included.Add(Candidate("b@example.test"));

        CreateAnnouncementResult result = await harness.Build().CreateAsync(
            Request(),
            preview.PlanHash,
            preview.ConfirmationPhrase,
            "Fakülte istedi.",
            Actor,
            "admin@example.test",
            CancellationToken.None);

        Assert.Equal(CreateAnnouncementOutcome.PlanChangedSincePreview, result.Outcome);
        Assert.Null(harness.Store.Saved);
    }

    [Fact]
    public async Task AWrongConfirmationPhraseWritesNothing()
    {
        Harness harness = new();
        harness.Audience.Included.Add(Candidate("a@example.test"));

        AnnouncementPreview preview =
            await harness.Build().PreviewAsync(Request(), CancellationToken.None);
        CreateAnnouncementResult result = await harness.Build().CreateAsync(
            Request(),
            preview.PlanHash,
            "99",
            "Fakülte istedi.",
            Actor,
            "admin@example.test",
            CancellationToken.None);

        Assert.Equal(CreateAnnouncementOutcome.ConfirmationMismatch, result.Outcome);
        Assert.Null(harness.Store.Saved);
    }

    [Fact]
    public async Task AnEmptyAudienceIsRefusedBecauseThereIsNothingToQueue()
    {
        Harness harness = new();

        AnnouncementPreview preview =
            await harness.Build().PreviewAsync(Request(), CancellationToken.None);
        CreateAnnouncementResult result = await harness.Build().CreateAsync(
            Request(),
            preview.PlanHash,
            preview.ConfirmationPhrase,
            "Fakülte istedi.",
            Actor,
            "admin@example.test",
            CancellationToken.None);

        Assert.Equal(CreateAnnouncementOutcome.NoRecipients, result.Outcome);
        Assert.Null(harness.Store.Saved);
    }

    [Fact]
    public async Task ARepeatedConfirmationIsAReplayRatherThanASecondCopy()
    {
        Harness harness = new();
        harness.Audience.Included.Add(Candidate("a@example.test"));
        harness.Store.Existing = ExistingSummary();

        CreateAnnouncementResult result = await harness.Build().CreateAsync(
            Request(),
            "irrelevant-hash",
            "irrelevant-phrase",
            "Fakülte istedi.",
            Actor,
            "admin@example.test",
            CancellationToken.None);

        // The replay check runs first: an announcement already on its way to a frozen recipient set
        // is not refused because the audience has drifted since — there is simply nothing to do.
        Assert.Equal(CreateAnnouncementOutcome.AlreadyExists, result.Outcome);
        Assert.Null(harness.Store.Saved);
    }

    [Fact]
    public async Task AWarningIsConfirmedByTheRecipientsOwnAddressNotByACountOfOne()
    {
        Harness harness = new();
        harness.Audience.Included.Add(Candidate("student@example.test"));
        Guid target = Guid.CreateVersion7();

        AnnouncementRequest request = Request() with
        {
            Kind = CalendarAnnouncementKind.UserWarning,
            TemplateKey = "profile-missing",
            Criteria = new AnnouncementAudienceCriteria
            {
                AcademicYear = "2025-2026",
                TargetUserId = target,
            },
        };

        AnnouncementPreview preview =
            await harness.Build().PreviewAsync(request, CancellationToken.None);

        Assert.Equal("student@example.test", preview.ConfirmationPhrase);
        Assert.Equal(
            $"warning:{target:N}:profile-missing:2026-11-12",
            preview.CampaignKey);
    }

    [Fact]
    public async Task AWarningToAnIneligibleUserStillNamesThemInTheConfirmationPhrase()
    {
        Harness harness = new();
        harness.Audience.Excluded.Add(
            Candidate("student@example.test", AnnouncementExclusionReason.NoCalendarConnection));

        AnnouncementPreview preview = await harness.Build().PreviewAsync(
            Request() with
            {
                Kind = CalendarAnnouncementKind.UserWarning,
                TemplateKey = "profile-missing",
                Criteria = new AnnouncementAudienceCriteria
                {
                    AcademicYear = "2025-2026",
                    TargetUserId = Guid.CreateVersion7(),
                },
            },
            CancellationToken.None);

        // The screen has to be able to say who was refused and why, so the phrase is derived from
        // the candidate rather than left blank when nobody is eligible.
        Assert.Equal(0, preview.RecipientCount);
        Assert.Equal("student@example.test", preview.ConfirmationPhrase);
    }

    private static AnnouncementRequest Request() => new()
    {
        Kind = CalendarAnnouncementKind.Bulk,
        Criteria = new AnnouncementAudienceCriteria
        {
            AcademicYear = "2025-2026",
            ClassYear = 2,
            ProgramLanguage = ProgramLanguage.Turkish,
        },
        Title = "Telafi dersi",
        Body = "12 Kasım Perşembe günü telafi dersi yapılacaktır.",
        IsAllDay = false,
        LocalDate = EventDate,
        StartLocalTime = new TimeOnly(9, 0),
        EndLocalTime = new TimeOnly(10, 0),
        CategoryKey = AnnouncementCategoryCatalog.DefaultKey,
    };

    private static AnnouncementAudienceCandidate Candidate(
        string email,
        AnnouncementExclusionReason? exclusion = null) => new()
        {
            UserId = Guid.CreateVersion7(),
            Email = email,
            ManagedCalendarId = exclusion is null ? $"cal-{email}" : null,
            ExclusionReason = exclusion,
        };

    private static AnnouncementSummary ExistingSummary() => new()
    {
        AnnouncementId = Guid.CreateVersion7(),
        Kind = CalendarAnnouncementKind.Bulk,
        CampaignKey = "bulk:2026-11-12:existing",
        Title = "Telafi dersi",
        Status = CalendarAnnouncementStatus.Delivered,
        ContentVersion = 1,
        LocalDate = EventDate,
        IsAllDay = false,
        RecipientCount = 12,
        Counts = new AnnouncementDeliveryCounts
        {
            Pending = 0,
            Written = 12,
            Skipped = 0,
            Removed = 0,
            Failed = 0,
        },
        CreatedBy = "admin@example.test",
        CreatedAtUtc = Now,
    };

    private sealed class Harness
    {
        public FakeAudienceStore Audience { get; } = new();

        public FakeAnnouncementStore Store { get; } = new();

        public AnnouncementService Build() =>
            new(Audience, Store, new FixedTimeProvider(Now));
    }

    private sealed class FakeAudienceStore : IAnnouncementAudienceReadStore
    {
        public List<AnnouncementAudienceCandidate> Included { get; } = [];

        public List<AnnouncementAudienceCandidate> Excluded { get; } = [];

        public Task<AnnouncementAudienceResolution> ResolveAsync(
            AnnouncementAudienceCriteria criteria,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AnnouncementAudienceResolution
            {
                Included = [.. Included],
                Excluded = [.. Excluded],
            });
    }

    private sealed class FakeAnnouncementStore : IAnnouncementStore
    {
        public AnnouncementSummary? Existing { get; set; }

        public CalendarAnnouncement? Saved { get; private set; }

        public List<CalendarAnnouncementDelivery> SavedDeliveries { get; } = [];

        public Task<AnnouncementSummary?> FindByCampaignKeyAsync(
            string campaignKey,
            CancellationToken cancellationToken) => Task.FromResult(Existing);

        public Task<AnnouncementCreateStoreResult> AddAsync(
            CalendarAnnouncement announcement,
            IReadOnlyList<CalendarAnnouncementDelivery> deliveries,
            CancellationToken cancellationToken)
        {
            Saved = announcement;
            SavedDeliveries.AddRange(deliveries);
            return Task.FromResult(new AnnouncementCreateStoreResult
            {
                AlreadyExisted = false,
                Announcement = new AnnouncementSummary
                {
                    AnnouncementId = announcement.Id,
                    Kind = announcement.Kind,
                    CampaignKey = announcement.CampaignKey,
                    Title = announcement.Title,
                    Status = announcement.Status,
                    ContentVersion = announcement.ContentVersion,
                    LocalDate = announcement.LocalDate,
                    IsAllDay = announcement.IsAllDay,
                    RecipientCount = announcement.RecipientCount,
                    Counts = new AnnouncementDeliveryCounts
                    {
                        Pending = deliveries.Count(delivery =>
                            delivery.State is CalendarAnnouncementDeliveryState.Pending),
                        Written = 0,
                        Skipped = deliveries.Count(delivery =>
                            delivery.State is CalendarAnnouncementDeliveryState.Skipped),
                        Removed = 0,
                        Failed = 0,
                    },
                    CreatedBy = announcement.CreatedBy,
                    CreatedAtUtc = announcement.CreatedAtUtc,
                },
            });
        }

        public Task<IReadOnlyList<AnnouncementSummary>> ListAsync(
            CalendarAnnouncementKind? kind,
            CalendarAnnouncementStatus? status,
            Guid? targetUserId,
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AnnouncementDetail?> FindAsync(
            Guid announcementId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PagedResult<AnnouncementDeliveryView>> ListDeliveriesAsync(
            Guid announcementId,
            CalendarAnnouncementDeliveryState? state,
            int page,
            int pageSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UpdateAnnouncementResult> UpdateContentAsync(
            Guid announcementId,
            AnnouncementContent content,
            string updatedBy,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CancelAnnouncementResult> RequestCancellationAsync(
            Guid announcementId,
            string cancelledBy,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AnnouncementDispatchCandidate>> ListDispatchableAsync(
            DateTimeOffset nowUtc,
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AnnouncementDeliveryTarget>> ListDeliveryTargetsAsync(
            Guid announcementId,
            CalendarAnnouncementDeliveryState state,
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkDeliveryWrittenAsync(
            Guid deliveryId,
            string googleEventId,
            int contentVersion,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkDeliverySkippedAsync(
            Guid deliveryId,
            AnnouncementExclusionReason reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkDeliveryFailedAsync(
            Guid deliveryId,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkDeliveryRemovedAsync(
            Guid deliveryId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ApplyDispatchOutcomeAsync(
            Guid announcementId,
            AnnouncementDispatchTransition transition,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeferAfterFailureAsync(
            Guid announcementId,
            string reason,
            DateTimeOffset nextAttemptAtUtc,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkDeliveryRunFailedAsync(
            Guid announcementId,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
