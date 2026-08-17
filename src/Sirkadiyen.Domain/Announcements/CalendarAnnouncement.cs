using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Domain.Announcements;

/// <summary>
/// A message an administrator wrote, addressed to managed calendars rather than to the schedule
/// (ADR-107).
/// </summary>
/// <remarks>
/// This is deliberately outside the schedule pipeline. It produces no canonical record, no
/// revision and no semantic diff, because it is not a claim about what the faculty published — it
/// is the product speaking to its users. It nevertheless obeys the same calendar rules: writes are
/// idempotent on a deterministic event id, the recipient set is materialized as a durable ledger
/// rather than recomputed, delivery is freeze-gated and budget-bounded, and a correction patches
/// the events already written instead of creating a second copy.
/// <para>
/// The recipient set is frozen at confirmation. An announcement is a decision about who is being
/// told something, so a student who changes cohort afterwards neither gains nor loses it; that is
/// what makes the confirmed count on the confirmation screen mean anything.
/// </para>
/// </remarks>
public sealed class CalendarAnnouncement
{
    public const int MaximumTitleLength = 200;

    public const int MaximumBodyLength = 4000;

    public const int MaximumLocationLength = 300;

    public const int MaximumInternalNoteLength = 2000;

    public const int MaximumActorLength = 320;

    public const int MaximumReasonLength = 2000;

    public const int MaximumFailureReasonLength = 2000;

    public const int MaximumTimeZoneIdLength = 100;

    public const int MaximumCategoryKeyLength = 64;

    public const int MaximumAcademicYearLength = 20;

    /// <summary>The longest reminder a calendar event may carry, in minutes (four weeks).</summary>
    public const int MaximumReminderMinutes = 40320;

    private CalendarAnnouncement()
    {
        // Materialization constructor.
        CampaignKey = string.Empty;
        Title = string.Empty;
        Body = string.Empty;
        TimeZoneId = string.Empty;
        CategoryKey = string.Empty;
        AudienceAcademicYear = string.Empty;
        AudienceSelectors = new Dictionary<string, string>(StringComparer.Ordinal);
        CreatedBy = string.Empty;
        CreationReason = string.Empty;
    }

    public Guid Id { get; private init; }

    public CalendarAnnouncementKind Kind { get; private init; }

    /// <summary>The deterministic dedup identity; unique across announcements.</summary>
    public string CampaignKey { get; private init; }

    /// <summary>The warning template this was composed from, when it was.</summary>
    public string? TemplateKey { get; private init; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public string? Location { get; private set; }

    public bool IsAllDay { get; private set; }

    public DateOnly LocalDate { get; private set; }

    /// <summary>Local wall-clock start; null exactly when the item is all-day (ADR-046).</summary>
    public TimeOnly? StartLocalTime { get; private set; }

    public TimeOnly? EndLocalTime { get; private set; }

    public string TimeZoneId { get; private set; }

    /// <summary>Minutes before the start at which the recipient is reminded, or null for none.</summary>
    public int? ReminderMinutesBefore { get; private set; }

    /// <summary>The presentation category deciding the calendar label's name and colour.</summary>
    public string CategoryKey { get; private set; }

    public string AudienceAcademicYear { get; private init; }

    /// <summary>The addressed class year, or null for every class year in the academic year.</summary>
    public int? AudienceClassYear { get; private init; }

    /// <summary>The addressed program language, or null for both.</summary>
    public ProgramLanguage? AudienceProgramLanguage { get; private init; }

    /// <summary>Cohort selectors the recipient's profile must match, all of them.</summary>
    public IReadOnlyDictionary<string, string> AudienceSelectors { get; private init; }

    /// <summary>The single addressed user for a warning; null for a bulk announcement.</summary>
    public Guid? TargetUserId { get; private init; }

    /// <summary>An operator-only note. It is never written to a calendar.</summary>
    public string? InternalNote { get; private set; }

    public CalendarAnnouncementStatus Status { get; private set; }

    /// <summary>
    /// Increments on every content change. A delivery whose applied version is lower is patched,
    /// which is how an edit reaches calendars without creating a second event.
    /// </summary>
    public int ContentVersion { get; private set; }

    /// <summary>The plan hash the confirmation was bound to, kept as evidence of the basis.</summary>
    public string? PlanHash { get; private set; }

    /// <summary>How many recipients were resolved as eligible at confirmation time.</summary>
    public int RecipientCount { get; private init; }

    /// <summary>How many resolved candidates were excluded, with reasons on the audit trail.</summary>
    public int ExcludedCount { get; private init; }

    public Guid CreatedByUserId { get; private init; }

    public string CreatedBy { get; private init; }

    public string CreationReason { get; private init; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public string? LastUpdatedBy { get; private set; }

    public string? LastUpdateReason { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public string? CancelledBy { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset? CancellationRequestedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>Delivery passes attempted; bounded so a broken announcement stops churning.</summary>
    public int DeliveryAttempts { get; private set; }

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public string? LastFailureReason { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    public static CalendarAnnouncement Create(
        CalendarAnnouncementKind kind,
        string campaignKey,
        string? templateKey,
        AnnouncementContent content,
        AnnouncementAudienceDefinition audience,
        string? planHash,
        int recipientCount,
        int excludedCount,
        Guid createdByUserId,
        string createdBy,
        string creationReason,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(audience);
        content.Validate();

        if (kind is CalendarAnnouncementKind.UserWarning && audience.TargetUserId is null)
        {
            throw new ArgumentException(
                "A user warning must name the user it is addressed to.",
                nameof(audience));
        }

        if (kind is CalendarAnnouncementKind.Bulk && audience.TargetUserId is not null)
        {
            throw new ArgumentException(
                "A bulk announcement is addressed to a cohort, not to one user.",
                nameof(audience));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("An announcement needs an author.", nameof(createdByUserId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(recipientCount);
        ArgumentOutOfRangeException.ThrowIfNegative(excludedCount);

        return new CalendarAnnouncement
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            CampaignKey = RequiredBounded(
                campaignKey,
                AnnouncementCampaignKey.MaximumLength,
                nameof(campaignKey)),
            TemplateKey = OptionalBounded(
                templateKey,
                AnnouncementCampaignKey.MaximumTemplateKeyLength,
                nameof(templateKey)),
            Title = content.Title.Trim(),
            Body = content.Body.Trim(),
            Location = OptionalBounded(content.Location, MaximumLocationLength, nameof(content)),
            IsAllDay = content.IsAllDay,
            LocalDate = content.LocalDate,
            StartLocalTime = content.StartLocalTime,
            EndLocalTime = content.EndLocalTime,
            TimeZoneId = content.TimeZoneId.Trim(),
            ReminderMinutesBefore = content.ReminderMinutesBefore,
            CategoryKey = content.CategoryKey.Trim(),
            InternalNote = OptionalBounded(
                content.InternalNote,
                MaximumInternalNoteLength,
                nameof(content)),
            AudienceAcademicYear = RequiredBounded(
                audience.AcademicYear,
                MaximumAcademicYearLength,
                nameof(audience)),
            AudienceClassYear = audience.ClassYear,
            AudienceProgramLanguage = audience.ProgramLanguage,
            AudienceSelectors = new Dictionary<string, string>(
                audience.Selectors,
                StringComparer.Ordinal),
            TargetUserId = audience.TargetUserId,
            PlanHash = OptionalBounded(planHash, 128, nameof(planHash)),
            RecipientCount = recipientCount,
            ExcludedCount = excludedCount,
            Status = CalendarAnnouncementStatus.Queued,
            ContentVersion = 1,
            CreatedByUserId = createdByUserId,
            CreatedBy = RequiredBounded(createdBy, MaximumActorLength, nameof(createdBy)),
            CreationReason = RequiredBounded(
                creationReason,
                MaximumReasonLength,
                nameof(creationReason)),
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };
    }

    /// <summary>
    /// Replaces what the announcement says. The recipient set is untouched — an edit corrects a
    /// message already addressed to specific people, and re-addressing it would be a new
    /// announcement with its own confirmation.
    /// </summary>
    public void UpdateContent(
        AnnouncementContent content,
        string updatedBy,
        string reason,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(content);
        content.Validate();

        if (Status is CalendarAnnouncementStatus.Cancelling or CalendarAnnouncementStatus.Cancelled)
        {
            throw new InvalidOperationException(
                "A cancelled announcement cannot be edited; compose a new one.");
        }

        Title = content.Title.Trim();
        Body = content.Body.Trim();
        Location = OptionalBounded(content.Location, MaximumLocationLength, nameof(content));
        IsAllDay = content.IsAllDay;
        LocalDate = content.LocalDate;
        StartLocalTime = content.StartLocalTime;
        EndLocalTime = content.EndLocalTime;
        TimeZoneId = content.TimeZoneId.Trim();
        ReminderMinutesBefore = content.ReminderMinutesBefore;
        CategoryKey = content.CategoryKey.Trim();
        InternalNote = OptionalBounded(
            content.InternalNote,
            MaximumInternalNoteLength,
            nameof(content));

        ContentVersion++;
        LastUpdatedBy = RequiredBounded(updatedBy, MaximumActorLength, nameof(updatedBy));
        LastUpdateReason = RequiredBounded(reason, MaximumReasonLength, nameof(reason));
        UpdatedAtUtc = atUtc;

        // The edit re-opens delivery: every written copy is now a version behind and has to be
        // patched. Attempts reset because this is new work, not a retry of the failed old work.
        Status = CalendarAnnouncementStatus.Queued;
        DeliveryAttempts = 0;
        NextAttemptAtUtc = null;
        LastFailureReason = null;
        CompletedAtUtc = null;
    }

    /// <summary>Records that a delivery pass has started, so a partial pass is visible.</summary>
    public void MarkDelivering(DateTimeOffset atUtc)
    {
        if (Status is CalendarAnnouncementStatus.Queued or CalendarAnnouncementStatus.Failed)
        {
            Status = CalendarAnnouncementStatus.Delivering;
        }

        DeliveryAttempts++;
        NextAttemptAtUtc = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Every delivery reached a terminal state. "Delivered" describes the campaign, not each
    /// recipient: some copies may be skipped or failed, and the counters say which.
    /// </summary>
    public void MarkDelivered(DateTimeOffset atUtc)
    {
        Status = CalendarAnnouncementStatus.Delivered;
        NextAttemptAtUtc = null;
        CompletedAtUtc = atUtc;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>Work remains but the pass yielded; not a failure, so nothing backs off.</summary>
    public void DeferForBudget(DateTimeOffset atUtc)
    {
        Status = CalendarAnnouncementStatus.Delivering;
        NextAttemptAtUtc = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>A transient failure: retry later with a growing delay.</summary>
    public void DeferAfterFailure(
        string reason,
        DateTimeOffset nextAttemptAtUtc,
        DateTimeOffset atUtc)
    {
        Status = CalendarAnnouncementStatus.Delivering;
        LastFailureReason = OptionalBounded(reason, MaximumFailureReasonLength, nameof(reason));
        NextAttemptAtUtc = nextAttemptAtUtc;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>The attempt cap was reached; an operator has to look at it.</summary>
    public void MarkFailed(string reason, DateTimeOffset atUtc)
    {
        Status = CalendarAnnouncementStatus.Failed;
        LastFailureReason = OptionalBounded(reason, MaximumFailureReasonLength, nameof(reason));
        NextAttemptAtUtc = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Asks for every written copy to be removed. Cancellation is a removal of what was written,
    /// never a claim that it was never sent: the delivery rows keep their history.
    /// </summary>
    public void RequestCancellation(string cancelledBy, string reason, DateTimeOffset atUtc)
    {
        if (Status is CalendarAnnouncementStatus.Cancelled)
        {
            return;
        }

        Status = CalendarAnnouncementStatus.Cancelling;
        CancelledBy = RequiredBounded(cancelledBy, MaximumActorLength, nameof(cancelledBy));
        CancellationReason = RequiredBounded(reason, MaximumReasonLength, nameof(reason));
        CancellationRequestedAtUtc = atUtc;
        NextAttemptAtUtc = null;
        DeliveryAttempts = 0;
        UpdatedAtUtc = atUtc;
    }

    public void MarkCancelled(DateTimeOffset atUtc)
    {
        Status = CalendarAnnouncementStatus.Cancelled;
        CancelledAtUtc = atUtc;
        CompletedAtUtc = atUtc;
        NextAttemptAtUtc = null;
        UpdatedAtUtc = atUtc;
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }

    private static string? OptionalBounded(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}

/// <summary>What an announcement says and when it happens, validated as one unit.</summary>
public sealed record AnnouncementContent
{
    public required string Title { get; init; }

    public required string Body { get; init; }

    public string? Location { get; init; }

    public required bool IsAllDay { get; init; }

    public required DateOnly LocalDate { get; init; }

    public TimeOnly? StartLocalTime { get; init; }

    public TimeOnly? EndLocalTime { get; init; }

    public required string TimeZoneId { get; init; }

    public int? ReminderMinutesBefore { get; init; }

    public required string CategoryKey { get; init; }

    public string? InternalNote { get; init; }

    /// <summary>
    /// Enforces the one invariant the calendar cannot express two ways: an item is either timed,
    /// with both local times, or all-day with neither (ADR-046, AI_GUIDELINE §10).
    /// </summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(Body);
        ArgumentException.ThrowIfNullOrWhiteSpace(TimeZoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(CategoryKey);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            Title.Trim().Length,
            CalendarAnnouncement.MaximumTitleLength,
            nameof(Title));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            Body.Trim().Length,
            CalendarAnnouncement.MaximumBodyLength,
            nameof(Body));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            TimeZoneId.Trim().Length,
            CalendarAnnouncement.MaximumTimeZoneIdLength,
            nameof(TimeZoneId));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            CategoryKey.Trim().Length,
            CalendarAnnouncement.MaximumCategoryKeyLength,
            nameof(CategoryKey));

        if (IsAllDay)
        {
            if (StartLocalTime is not null || EndLocalTime is not null)
            {
                throw new ArgumentException(
                    "An all-day announcement carries no times at all.",
                    nameof(IsAllDay));
            }
        }
        else
        {
            if (StartLocalTime is null || EndLocalTime is null)
            {
                throw new ArgumentException(
                    "A timed announcement needs both a start and an end time.",
                    nameof(StartLocalTime));
            }

            if (EndLocalTime <= StartLocalTime)
            {
                throw new ArgumentException(
                    "An announcement must end after it starts.",
                    nameof(EndLocalTime));
            }
        }

        if (ReminderMinutesBefore is { } reminder)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(reminder, nameof(ReminderMinutesBefore));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                reminder,
                CalendarAnnouncement.MaximumReminderMinutes,
                nameof(ReminderMinutesBefore));
        }
    }
}

/// <summary>Who an announcement is addressed to, before eligibility is applied.</summary>
public sealed record AnnouncementAudienceDefinition
{
    public required string AcademicYear { get; init; }

    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    /// <summary>Cohort selectors every recipient's profile must match.</summary>
    public IReadOnlyDictionary<string, string> Selectors { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Set only for a single-user warning.</summary>
    public Guid? TargetUserId { get; init; }
}
