namespace Sirkadiyen.Domain.Announcements;

/// <summary>
/// Who an administrator-authored calendar announcement is addressed to (ADR-107).
/// </summary>
/// <remarks>
/// Both kinds share one aggregate because they are the same act — writing a message an
/// administrator wrote onto managed calendars — and differ only in how the recipient set is
/// decided. Splitting them would duplicate the delivery ledger, the idempotency key, the
/// freeze gate and the cancel path, all of which are high-risk calendar code.
/// </remarks>
public enum CalendarAnnouncementKind
{
    /// <summary>Addressed to an academic cohort, resolved from profile dimensions.</summary>
    Bulk,

    /// <summary>Addressed to exactly one named user, usually from a template.</summary>
    UserWarning,
}

/// <summary>The lifecycle of one announcement, from confirmation to removal.</summary>
public enum CalendarAnnouncementStatus
{
    /// <summary>Confirmed and recipient-resolved; no calendar has been written yet.</summary>
    Queued,

    /// <summary>At least one delivery has been attempted and some remain.</summary>
    Delivering,

    /// <summary>Every delivery reached a terminal state (written, skipped or failed).</summary>
    Delivered,

    /// <summary>An operator cancelled it; written events are being removed.</summary>
    Cancelling,

    /// <summary>Every written event has been removed.</summary>
    Cancelled,

    /// <summary>Delivery failed for the whole announcement after the capped attempts.</summary>
    Failed,
}

/// <summary>What has happened to one recipient's copy of an announcement.</summary>
public enum CalendarAnnouncementDeliveryState
{
    /// <summary>Nothing has been written yet, or the content moved and needs a patch.</summary>
    Pending,

    /// <summary>The event exists on the recipient's managed calendar at the applied version.</summary>
    Written,

    /// <summary>The recipient could not receive it; <c>SkipReason</c> says why.</summary>
    Skipped,

    /// <summary>The event was written and has since been removed by a cancellation.</summary>
    Removed,

    /// <summary>Writing failed for a reason that is neither a skip nor transient.</summary>
    Failed,
}

/// <summary>
/// Why a resolved candidate cannot receive an announcement. These are eligibility facts, not
/// operator-chosen filters: none of them can be waived, because there is no calendar to write to.
/// </summary>
public enum AnnouncementExclusionReason
{
    /// <summary>The account has no academic profile, so it belongs to no cohort.</summary>
    NoStudentProfile,

    /// <summary>No active license, so synchronization has stopped for them (ADR-095).</summary>
    LicenseInactive,

    /// <summary>The account never granted Calendar access.</summary>
    NoCalendarConnection,

    /// <summary>The stored grant was revoked or expired and awaits re-authorization.</summary>
    CalendarAuthorizationRevoked,

    /// <summary>Their initial synchronization has not finished, so there is no calendar yet.</summary>
    InitialSyncIncomplete,

    /// <summary>The managed calendar is missing or inaccessible and needs repair.</summary>
    ManagedCalendarUnavailable,
}
