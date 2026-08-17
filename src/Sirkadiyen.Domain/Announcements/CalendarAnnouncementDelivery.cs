namespace Sirkadiyen.Domain.Announcements;

/// <summary>
/// One recipient's copy of an announcement, and the durable ledger the delivery worker resumes
/// from (ADR-107).
/// </summary>
/// <remarks>
/// Unlike a schedule fan-out, whose progress lives in the shared calendar-event mapping ledger
/// (ADR-059 §27), an announcement needs a row per recipient of its own: the counters the operator
/// is shown — written, skipped, failed, pending — are precisely this table, and a skip has to
/// carry the reason it happened. The row is created at confirmation, which is also what freezes
/// the recipient set.
/// </remarks>
public sealed class CalendarAnnouncementDelivery
{
    public const int MaximumGoogleCalendarIdLength = 1024;

    public const int MaximumGoogleEventIdLength = 1024;

    public const int MaximumFailureReasonLength = 2000;

    private CalendarAnnouncementDelivery()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid CalendarAnnouncementId { get; private init; }

    public Guid UserId { get; private init; }

    /// <summary>The recipient's dedicated calendar, as it stood when the row was created.</summary>
    public string? GoogleCalendarId { get; private set; }

    /// <summary>The deterministic event id this copy is written under.</summary>
    public string? GoogleEventId { get; private set; }

    public CalendarAnnouncementDeliveryState State { get; private set; }

    /// <summary>Which content version the recipient currently holds, if any.</summary>
    public int? AppliedContentVersion { get; private set; }

    public AnnouncementExclusionReason? SkipReason { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>A recipient the announcement will be written to on the next delivery pass.</summary>
    public static CalendarAnnouncementDelivery Pending(
        Guid announcementId,
        Guid userId,
        string googleCalendarId,
        DateTimeOffset atUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            CalendarAnnouncementId = Required(announcementId, nameof(announcementId)),
            UserId = Required(userId, nameof(userId)),
            GoogleCalendarId = Bounded(
                googleCalendarId,
                MaximumGoogleCalendarIdLength,
                nameof(googleCalendarId)),
            State = CalendarAnnouncementDeliveryState.Pending,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };

    /// <summary>
    /// A candidate who cannot receive it, recorded rather than omitted. The operator saw this
    /// exclusion and its reason before confirming, so dropping the row afterwards would erase the
    /// evidence for what they approved (plan §4.4).
    /// </summary>
    public static CalendarAnnouncementDelivery Excluded(
        Guid announcementId,
        Guid userId,
        AnnouncementExclusionReason reason,
        DateTimeOffset atUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            CalendarAnnouncementId = Required(announcementId, nameof(announcementId)),
            UserId = Required(userId, nameof(userId)),
            State = CalendarAnnouncementDeliveryState.Skipped,
            SkipReason = reason,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };

    public void MarkWritten(string googleEventId, int contentVersion, DateTimeOffset atUtc)
    {
        GoogleEventId = Bounded(googleEventId, MaximumGoogleEventIdLength, nameof(googleEventId));
        AppliedContentVersion = contentVersion;
        State = CalendarAnnouncementDeliveryState.Written;
        FailureReason = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// The recipient became ineligible between confirmation and delivery — most often a revoked
    /// grant. Their copy is skipped with the reason; nothing already on their calendar is touched.
    /// </summary>
    public void MarkSkipped(AnnouncementExclusionReason reason, DateTimeOffset atUtc)
    {
        State = CalendarAnnouncementDeliveryState.Skipped;
        SkipReason = reason;
        UpdatedAtUtc = atUtc;
    }

    public void MarkFailed(string reason, DateTimeOffset atUtc)
    {
        State = CalendarAnnouncementDeliveryState.Failed;
        FailureReason = Optional(reason, MaximumFailureReasonLength);
        UpdatedAtUtc = atUtc;
    }

    /// <summary>The written copy was removed by a cancellation; the row keeps the history.</summary>
    public void MarkRemoved(DateTimeOffset atUtc)
    {
        State = CalendarAnnouncementDeliveryState.Removed;
        AppliedContentVersion = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Returns a written copy to the queue after a content edit, so the next pass patches it.
    /// A skipped or removed copy is deliberately left alone: an edit does not make an ineligible
    /// recipient eligible, and it must not resurrect a cancelled event.
    /// </summary>
    public void ReopenForPatch(DateTimeOffset atUtc)
    {
        if (State is not (CalendarAnnouncementDeliveryState.Written
            or CalendarAnnouncementDeliveryState.Failed))
        {
            return;
        }

        State = CalendarAnnouncementDeliveryState.Pending;
        FailureReason = null;
        UpdatedAtUtc = atUtc;
    }

    private static Guid Required(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A required identifier was empty.", parameterName);
        }

        return value;
    }

    private static string Bounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maximumLength)];
}
