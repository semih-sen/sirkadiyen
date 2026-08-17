using System.Globalization;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Announcements;

namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// The six-step high-risk operation behind both announcement screens (plan §4.3, ADR-107):
/// choose a scope, compute its impact on the server, show inclusions and exclusions with reasons,
/// preview exactly what will be written, take a bound confirmation, then queue and track it.
/// </summary>
/// <remarks>
/// Steps two through five live here rather than in the endpoint, so the browser never decides who
/// receives an announcement and cannot confirm a plan the server did not compute
/// (AI_GUIDELINE §6, §16).
/// </remarks>
public sealed class AnnouncementService(
    IAnnouncementAudienceReadStore audienceStore,
    IAnnouncementStore store,
    TimeProvider timeProvider)
{
    /// <summary>Schedules are interpreted in Istanbul, and so is an announcement (§18).</summary>
    public const string TimeZoneId = "Europe/Istanbul";

    /// <summary>How many recipients and exclusions a preview lists by name.</summary>
    public const int SampleSize = 50;

    public async Task<AnnouncementPreview> PreviewAsync(
        AnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AnnouncementContent content = request.ToContent();
        content.Validate();

        AnnouncementAudienceResolution resolution =
            await audienceStore.ResolveAsync(request.Criteria, cancellationToken);
        string campaignKey = CampaignKeyFor(request, content);
        AnnouncementSummary? existing =
            await store.FindByCampaignKeyAsync(campaignKey, cancellationToken);

        return new AnnouncementPreview
        {
            CampaignKey = campaignKey,
            PlanHash = AnnouncementPlanHasher.Compute(
                campaignKey,
                content,
                request.Criteria,
                resolution),
            RecipientCount = resolution.Included.Count,
            ExcludedCount = resolution.Excluded.Count,
            Exclusions = GroupExclusions(resolution.Excluded),
            Recipients = [.. resolution.Included.Take(SampleSize)],
            ExcludedRecipients = [.. resolution.Excluded.Take(SampleSize)],
            ExistingAnnouncement = existing,
            ConfirmationPhrase = ConfirmationPhrase(request, resolution),
        };
    }

    public async Task<CreateAnnouncementResult> CreateAsync(
        AnnouncementRequest request,
        string planHash,
        string confirmationPhrase,
        string reason,
        Guid actorUserId,
        string actorEmail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AnnouncementContent content = request.ToContent();
        content.Validate();

        AnnouncementAudienceResolution resolution =
            await audienceStore.ResolveAsync(request.Criteria, cancellationToken);
        string campaignKey = CampaignKeyFor(request, content);

        // The replay check runs before the plan check. An announcement already delivered under
        // this key has a frozen recipient set, so an audience that has drifted since is not a
        // reason to refuse — there is simply nothing left to do (plan §4.4 deduplication).
        if (await store.FindByCampaignKeyAsync(campaignKey, cancellationToken) is { } existing)
        {
            return new CreateAnnouncementResult
            {
                Outcome = CreateAnnouncementOutcome.AlreadyExists,
                Announcement = existing,
            };
        }

        string recomputed = AnnouncementPlanHasher.Compute(
            campaignKey,
            content,
            request.Criteria,
            resolution);
        if (!string.Equals(recomputed, planHash?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new CreateAnnouncementResult
            {
                Outcome = CreateAnnouncementOutcome.PlanChangedSincePreview,
                Detail = "Onayladığınız önizlemeden bu yana kitle veya içerik değişti. "
                    + "Yeniden önizleyip yeni alıcı listesini görün.",
            };
        }

        if (!string.Equals(
            ConfirmationPhrase(request, resolution),
            confirmationPhrase?.Trim(),
            StringComparison.OrdinalIgnoreCase))
        {
            return new CreateAnnouncementResult
            {
                Outcome = CreateAnnouncementOutcome.ConfirmationMismatch,
                Detail = "Onay ifadesi eşleşmedi.",
            };
        }

        if (resolution.Included.Count == 0)
        {
            return new CreateAnnouncementResult
            {
                Outcome = CreateAnnouncementOutcome.NoRecipients,
                Detail = "Bu kitlede etkinliği alabilecek kimse yok, bu yüzden kuyruğa alınacak "
                    + "bir gönderim de yok.",
            };
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        CalendarAnnouncement announcement = CalendarAnnouncement.Create(
            request.Kind,
            campaignKey,
            request.TemplateKey,
            content,
            new AnnouncementAudienceDefinition
            {
                AcademicYear = request.Criteria.AcademicYear,
                ClassYear = request.Criteria.ClassYear,
                ProgramLanguage = request.Criteria.ProgramLanguage,
                Selectors = request.Criteria.Selectors,
                TargetUserId = request.Criteria.TargetUserId,
            },
            recomputed,
            resolution.Included.Count,
            resolution.Excluded.Count,
            actorUserId,
            actorEmail,
            reason,
            now);

        // Both halves of the resolution become rows. The excluded ones are the evidence for what
        // the operator approved: dropping them would leave the confirmed exclusion counts with
        // nothing behind them a day later.
        List<CalendarAnnouncementDelivery> deliveries =
        [
            .. resolution.Included.Select(candidate => CalendarAnnouncementDelivery.Pending(
                announcement.Id,
                candidate.UserId,
                candidate.ManagedCalendarId!,
                now)),
            .. resolution.Excluded.Select(candidate => CalendarAnnouncementDelivery.Excluded(
                announcement.Id,
                candidate.UserId,
                candidate.ExclusionReason ?? AnnouncementExclusionReason.NoCalendarConnection,
                now)),
        ];

        AnnouncementCreateStoreResult stored =
            await store.AddAsync(announcement, deliveries, cancellationToken);

        return new CreateAnnouncementResult
        {
            Outcome = stored.AlreadyExisted
                ? CreateAnnouncementOutcome.AlreadyExists
                : CreateAnnouncementOutcome.Queued,
            Announcement = stored.Announcement,
        };
    }

    public Task<IReadOnlyList<AnnouncementSummary>> ListAsync(
        CalendarAnnouncementKind? kind,
        CalendarAnnouncementStatus? status,
        int limit,
        CancellationToken cancellationToken) =>
        store.ListAsync(kind, status, limit, cancellationToken);

    public Task<AnnouncementDetail?> FindAsync(
        Guid announcementId,
        CancellationToken cancellationToken) =>
        store.FindAsync(announcementId, cancellationToken);

    public Task<PagedResult<AnnouncementDeliveryView>> ListDeliveriesAsync(
        Guid announcementId,
        CalendarAnnouncementDeliveryState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        store.ListDeliveriesAsync(announcementId, state, page, pageSize, cancellationToken);

    /// <summary>
    /// Corrects what an announcement says. It never re-resolves the audience: the recipients were
    /// decided at confirmation, and a correction reaching a different set of people would be a new
    /// announcement pretending to be an edit.
    /// </summary>
    public Task<UpdateAnnouncementResult> UpdateAsync(
        Guid announcementId,
        AnnouncementRequest request,
        string updatedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AnnouncementContent content = request.ToContent();
        content.Validate();

        return store.UpdateContentAsync(
            announcementId,
            content,
            updatedBy,
            reason,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<CancelAnnouncementResult> CancelAsync(
        Guid announcementId,
        string cancelledBy,
        string reason,
        CancellationToken cancellationToken) =>
        store.RequestCancellationAsync(
            announcementId,
            cancelledBy,
            reason,
            timeProvider.GetUtcNow(),
            cancellationToken);

    /// <summary>The earliest local date an announcement may be written for.</summary>
    public DateOnly EarliestLocalDate()
    {
        TimeZoneInfo istanbul = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), istanbul).DateTime);
    }

    private static string CampaignKeyFor(AnnouncementRequest request, AnnouncementContent content) =>
        request.Kind is CalendarAnnouncementKind.UserWarning
            ? AnnouncementCampaignKey.ForUserWarning(
                request.Criteria.TargetUserId!.Value,
                request.TemplateKey ?? "custom",
                content.LocalDate)
            : AnnouncementCampaignKey.ForBulk(
                request.Criteria.AcademicYear,
                request.Criteria.ClassYear,
                request.Criteria.ProgramLanguage?.ToString(),
                request.Criteria.Selectors,
                content.LocalDate,
                content.Title);

    /// <summary>
    /// What the operator has to type to confirm (plan §4.3 step 5). A bulk announcement is
    /// confirmed by its recipient count, because the number of people affected is the fact that
    /// should be hardest to overlook; a warning is confirmed by the recipient's own address, since
    /// its count is always one and would confirm nothing.
    /// </summary>
    private static string ConfirmationPhrase(
        AnnouncementRequest request,
        AnnouncementAudienceResolution resolution) =>
        request.Kind is CalendarAnnouncementKind.UserWarning
            ? resolution.Included.FirstOrDefault()?.Email
                ?? resolution.Excluded.FirstOrDefault()?.Email
                ?? "—"
            : resolution.Included.Count.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<AnnouncementExclusionGroup> GroupExclusions(
        IReadOnlyList<AnnouncementAudienceCandidate> excluded) =>
        [
            .. excluded
                .GroupBy(candidate =>
                    candidate.ExclusionReason ?? AnnouncementExclusionReason.NoCalendarConnection)
                .OrderBy(group => group.Key)
                .Select(group => new AnnouncementExclusionGroup
                {
                    Reason = group.Key,
                    Count = group.Count(),
                }),
        ];
}

/// <summary>
/// What an operator composed, before the server decides anything about it. The same shape serves
/// preview, confirmation and edit, so a preview can never describe a different request than the
/// one that follows it.
/// </summary>
public sealed record AnnouncementRequest
{
    public required CalendarAnnouncementKind Kind { get; init; }

    public required AnnouncementAudienceCriteria Criteria { get; init; }

    public string? TemplateKey { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public string? Location { get; init; }

    public required bool IsAllDay { get; init; }

    public required DateOnly LocalDate { get; init; }

    public TimeOnly? StartLocalTime { get; init; }

    public TimeOnly? EndLocalTime { get; init; }

    public int? ReminderMinutesBefore { get; init; }

    public required string CategoryKey { get; init; }

    public string? InternalNote { get; init; }

    public AnnouncementContent ToContent() => new()
    {
        Title = Title,
        Body = Body,
        Location = Location,
        IsAllDay = IsAllDay,
        LocalDate = LocalDate,
        StartLocalTime = StartLocalTime,
        EndLocalTime = EndLocalTime,
        // Not operator-chosen: every Sirkadiyen schedule is interpreted in one zone, and letting
        // an announcement pick another would put it at a different moment than the lessons around
        // it for no stated reason.
        TimeZoneId = AnnouncementService.TimeZoneId,
        ReminderMinutesBefore = ReminderMinutesBefore,
        CategoryKey = CategoryKey,
        InternalNote = InternalNote,
    };
}
