using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// Persists announcements and their per-recipient delivery ledger (ADR-107).
/// </summary>
public interface IAnnouncementStore
{
    /// <summary>The announcement holding this campaign key, if one already does.</summary>
    Task<AnnouncementSummary?> FindByCampaignKeyAsync(
        string campaignKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the announcement and every delivery row in one transaction. A campaign key that
    /// already exists is reported rather than thrown, because a repeated confirmation is a replay
    /// and must not look like an error to the operator.
    /// </summary>
    Task<AnnouncementCreateStoreResult> AddAsync(
        CalendarAnnouncement announcement,
        IReadOnlyList<CalendarAnnouncementDelivery> deliveries,
        CancellationToken cancellationToken);

    /// <summary>
    /// Announcements, newest first. <paramref name="targetUserId"/> narrows to the warnings written
    /// to one account, which is what an operator reading that account needs — the delivery ledger
    /// answers "did it reach them", but only this answers "what have we already told them".
    /// </summary>
    Task<IReadOnlyList<AnnouncementSummary>> ListAsync(
        CalendarAnnouncementKind? kind,
        CalendarAnnouncementStatus? status,
        Guid? targetUserId,
        int limit,
        CancellationToken cancellationToken);

    Task<AnnouncementDetail?> FindAsync(Guid announcementId, CancellationToken cancellationToken);

    Task<PagedResult<AnnouncementDeliveryView>> ListDeliveriesAsync(
        Guid announcementId,
        CalendarAnnouncementDeliveryState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the content and returns every written copy to the queue for a patch, in one
    /// transaction — otherwise a crash between the two would leave recipients holding an old
    /// version no pass would ever correct.
    /// </summary>
    Task<UpdateAnnouncementResult> UpdateContentAsync(
        Guid announcementId,
        AnnouncementContent content,
        string updatedBy,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task<CancelAnnouncementResult> RequestCancellationAsync(
        Guid announcementId,
        string cancelledBy,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    // ---- Delivery worker ---------------------------------------------------

    /// <summary>
    /// Announcements with work outstanding: queued, mid-delivery, or cancelling. A deferred one is
    /// returned only once its back-off has elapsed.
    /// </summary>
    Task<IReadOnlyList<AnnouncementDispatchCandidate>> ListDispatchableAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// The next recipients to act on, with their eligibility recomputed as it stands now rather
    /// than as it stood at confirmation — a grant can die in between, and writing to a dead one is
    /// a failure the campaign must survive.
    /// </summary>
    Task<IReadOnlyList<AnnouncementDeliveryTarget>> ListDeliveryTargetsAsync(
        Guid announcementId,
        CalendarAnnouncementDeliveryState state,
        int limit,
        CancellationToken cancellationToken);

    Task MarkDeliveryWrittenAsync(
        Guid deliveryId,
        string googleEventId,
        int contentVersion,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task MarkDeliverySkippedAsync(
        Guid deliveryId,
        AnnouncementExclusionReason reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task MarkDeliveryFailedAsync(
        Guid deliveryId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task MarkDeliveryRemovedAsync(
        Guid deliveryId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    Task ApplyDispatchOutcomeAsync(
        Guid announcementId,
        AnnouncementDispatchTransition transition,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>A transient failure: the pass is retried after the supplied delay.</summary>
    Task DeferAfterFailureAsync(
        Guid announcementId,
        string reason,
        DateTimeOffset nextAttemptAtUtc,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>The attempt cap was reached; delivery stops until an operator acts.</summary>
    Task MarkDeliveryRunFailedAsync(
        Guid announcementId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

/// <summary>The result of persisting a confirmed announcement.</summary>
public sealed record AnnouncementCreateStoreResult
{
    public required bool AlreadyExisted { get; init; }

    public required AnnouncementSummary Announcement { get; init; }
}

/// <summary>Everything the delivery worker needs to build one recipient's event.</summary>
public sealed record AnnouncementDispatchCandidate
{
    public required Guid AnnouncementId { get; init; }

    public required CalendarAnnouncementKind Kind { get; init; }

    public required CalendarAnnouncementStatus Status { get; init; }

    public required int ContentVersion { get; init; }

    public required int DeliveryAttempts { get; init; }

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
}

/// <summary>One recipient row plus their eligibility as it stands at delivery time.</summary>
public sealed record AnnouncementDeliveryTarget
{
    public required Guid DeliveryId { get; init; }

    public required Guid UserId { get; init; }

    /// <summary>Null when the recipient is currently ineligible.</summary>
    public string? ProtectedRefreshToken { get; init; }

    public string? ManagedCalendarId { get; init; }

    /// <summary>The recipient's own class/program, used to resolve the scoped freeze.</summary>
    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    /// <summary>Null when the copy may be written now; otherwise why it may not.</summary>
    public AnnouncementExclusionReason? CurrentExclusion { get; init; }

    public string? GoogleEventId { get; init; }

    public int? AppliedContentVersion { get; init; }
}

/// <summary>What one delivery pass concluded about the announcement as a whole.</summary>
public enum AnnouncementDispatchTransition
{
    /// <summary>A pass has begun; the attempt is counted.</summary>
    Started,

    /// <summary>Every recipient reached a terminal state.</summary>
    Completed,

    /// <summary>The per-cycle budget was reached. Not a failure, so no back-off.</summary>
    DeferredForBudget,

    /// <summary>Every written copy has been removed.</summary>
    Cancelled,
}
