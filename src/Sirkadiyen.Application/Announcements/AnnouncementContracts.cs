using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// The audience an operator addressed, as the resolver reads it (ADR-107).
/// </summary>
/// <remarks>
/// There is deliberately no "account status", "license status" or "sync eligibility" filter here,
/// although the design plan lists them among the audience dimensions. They are not choices: an
/// account with no active license has stopped synchronizing (ADR-095), and an account with no
/// completed initial sync has no managed calendar to write to. Offering them as toggles would
/// promise the operator an outcome the calendar cannot deliver, so they appear on the other side —
/// as <see cref="AnnouncementExclusionReason"/> values the operator reads before confirming.
/// </remarks>
public sealed record AnnouncementAudienceCriteria
{
    public required string AcademicYear { get; init; }

    /// <summary>Null addresses every class year in the academic year.</summary>
    public int? ClassYear { get; init; }

    /// <summary>Null addresses both program languages.</summary>
    public ProgramLanguage? ProgramLanguage { get; init; }

    /// <summary>Selectors a recipient's profile must match — all of them, not any.</summary>
    public IReadOnlyDictionary<string, string> Selectors { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Set for a single-user warning; every other criterion is then ignored.</summary>
    public Guid? TargetUserId { get; init; }
}

/// <summary>One account the audience matched, with what it can and cannot receive.</summary>
public sealed record AnnouncementAudienceCandidate
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    /// <summary>The calendar the copy would be written to; null exactly when excluded.</summary>
    public string? ManagedCalendarId { get; init; }

    /// <summary>Null for an eligible recipient; otherwise why the copy cannot be written.</summary>
    public AnnouncementExclusionReason? ExclusionReason { get; init; }
}

/// <summary>The full resolution of one audience: who receives it, and who cannot.</summary>
public sealed record AnnouncementAudienceResolution
{
    public required IReadOnlyList<AnnouncementAudienceCandidate> Included { get; init; }

    public required IReadOnlyList<AnnouncementAudienceCandidate> Excluded { get; init; }
}

/// <summary>
/// The server-computed, binding preview an operator confirms against (plan §4.3 step 4).
/// </summary>
public sealed record AnnouncementPreview
{
    /// <summary>The deterministic dedup key, shown before anything is written (plan §4.4).</summary>
    public required string CampaignKey { get; init; }

    /// <summary>
    /// A hash of exactly what was previewed. The confirmation carries it back and the write is
    /// refused if it no longer matches, so an operator can never approve one plan and execute
    /// another (the FinanceDistribution pattern, ADR-093).
    /// </summary>
    public required string PlanHash { get; init; }

    public required int RecipientCount { get; init; }

    public required int ExcludedCount { get; init; }

    /// <summary>How many excluded candidates fall under each reason.</summary>
    public required IReadOnlyList<AnnouncementExclusionGroup> Exclusions { get; init; }

    /// <summary>A bounded sample of recipients, so the operator sees who this really is.</summary>
    public required IReadOnlyList<AnnouncementAudienceCandidate> Recipients { get; init; }

    public required IReadOnlyList<AnnouncementAudienceCandidate> ExcludedRecipients { get; init; }

    /// <summary>
    /// The announcement that already holds this campaign key, when one does. Confirming would
    /// then be a replay: nothing new is written and no recipient receives a second copy.
    /// </summary>
    public AnnouncementSummary? ExistingAnnouncement { get; init; }

    /// <summary>The exact phrase the operator has to type to confirm (plan §4.3 step 5).</summary>
    public required string ConfirmationPhrase { get; init; }
}

public sealed record AnnouncementExclusionGroup
{
    public required AnnouncementExclusionReason Reason { get; init; }

    public required int Count { get; init; }
}

/// <summary>A list projection of one announcement and how its delivery is going.</summary>
public sealed record AnnouncementSummary
{
    public required Guid AnnouncementId { get; init; }

    public required CalendarAnnouncementKind Kind { get; init; }

    public required string CampaignKey { get; init; }

    public required string Title { get; init; }

    public required CalendarAnnouncementStatus Status { get; init; }

    public required int ContentVersion { get; init; }

    public required DateOnly LocalDate { get; init; }

    public required bool IsAllDay { get; init; }

    public TimeOnly? StartLocalTime { get; init; }

    public TimeOnly? EndLocalTime { get; init; }

    public required int RecipientCount { get; init; }

    public required AnnouncementDeliveryCounts Counts { get; init; }

    public required string CreatedBy { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? LastFailureReason { get; init; }

    public string? CancelledBy { get; init; }

    public string? CancellationReason { get; init; }
}

/// <summary>
/// The live delivery counters, read from the delivery ledger rather than stored on the
/// announcement, so they can never disagree with the rows they summarize.
/// </summary>
public sealed record AnnouncementDeliveryCounts
{
    public required int Pending { get; init; }

    public required int Written { get; init; }

    public required int Skipped { get; init; }

    public required int Removed { get; init; }

    public required int Failed { get; init; }

    public int Total => Pending + Written + Skipped + Removed + Failed;
}

/// <summary>One announcement in full, including what it says and who authored it.</summary>
public sealed record AnnouncementDetail
{
    public required AnnouncementSummary Summary { get; init; }

    public required string Body { get; init; }

    public string? Location { get; init; }

    public required string TimeZoneId { get; init; }

    public int? ReminderMinutesBefore { get; init; }

    public required string CategoryKey { get; init; }

    public string? TemplateKey { get; init; }

    public string? InternalNote { get; init; }

    public required string AudienceAcademicYear { get; init; }

    public int? AudienceClassYear { get; init; }

    public ProgramLanguage? AudienceProgramLanguage { get; init; }

    public required IReadOnlyDictionary<string, string> AudienceSelectors { get; init; }

    public Guid? TargetUserId { get; init; }

    public required string CreationReason { get; init; }

    public string? LastUpdatedBy { get; init; }

    public string? LastUpdateReason { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public string? PlanHash { get; init; }

    public required int DeliveryAttempts { get; init; }

    /// <summary>Excluded candidates grouped by reason, kept from confirmation time.</summary>
    public required IReadOnlyList<AnnouncementExclusionGroup> Exclusions { get; init; }
}

/// <summary>One recipient row of the delivery ledger.</summary>
public sealed record AnnouncementDeliveryView
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    public required CalendarAnnouncementDeliveryState State { get; init; }

    public AnnouncementExclusionReason? SkipReason { get; init; }

    public int? AppliedContentVersion { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>The outcome of confirming an announcement.</summary>
public sealed record CreateAnnouncementResult
{
    public required CreateAnnouncementOutcome Outcome { get; init; }

    public AnnouncementSummary? Announcement { get; init; }

    /// <summary>Set when the outcome explains a refusal in words the operator can act on.</summary>
    public string? Detail { get; init; }
}

public enum CreateAnnouncementOutcome
{
    /// <summary>Recorded and queued; the worker writes it.</summary>
    Queued,

    /// <summary>
    /// The campaign key already exists, so this was a replay. Nothing new was written and the
    /// existing announcement is returned unchanged (plan §4.4 deduplication).
    /// </summary>
    AlreadyExists,

    /// <summary>The audience resolved differently from the preview the operator approved.</summary>
    PlanChangedSincePreview,

    /// <summary>The typed confirmation phrase did not match the required one.</summary>
    ConfirmationMismatch,

    /// <summary>The audience resolved to nobody, so there is nothing to queue.</summary>
    NoRecipients,

    /// <summary>The request itself is not a valid announcement; <c>Detail</c> says why.</summary>
    Invalid,
}

public sealed record UpdateAnnouncementResult
{
    public required UpdateAnnouncementOutcome Outcome { get; init; }

    public AnnouncementSummary? Announcement { get; init; }

    public string? Detail { get; init; }
}

public enum UpdateAnnouncementOutcome
{
    /// <summary>The content changed; every written copy is queued for a patch.</summary>
    Updated,

    NotFound,

    /// <summary>A cancelled announcement cannot be edited; compose a new one.</summary>
    Cancelled,

    Invalid,

    /// <summary>Another operator changed it first; read it again.</summary>
    ConcurrentChange,
}

public sealed record CancelAnnouncementResult
{
    public required CancelAnnouncementOutcome Outcome { get; init; }

    public AnnouncementSummary? Announcement { get; init; }
}

public enum CancelAnnouncementOutcome
{
    /// <summary>Removal was requested; the worker deletes each written copy.</summary>
    CancellationRequested,

    /// <summary>Every copy was already removed.</summary>
    AlreadyCancelled,

    NotFound,

    ConcurrentChange,
}
