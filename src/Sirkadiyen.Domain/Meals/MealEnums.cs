namespace Sirkadiyen.Domain.Meals;

/// <summary>
/// Which of the day's meals a menu describes (ADR-150). Mirrors the faculty API's
/// <c>category</c> vocabulary; only <see cref="Lunch"/> is acquired today, but the dimension is
/// part of a menu-day's identity so a second meal can be added without reshaping the schema.
/// </summary>
public enum MealCategory
{
    Breakfast,
    Lunch,
    Dinner,
}

/// <summary>The lifecycle of one acquired menu-day (ADR-150).</summary>
public enum MealMenuDayStatus
{
    /// <summary>The faculty published a menu for this date and it is current.</summary>
    Published,

    /// <summary>
    /// A previously published menu stopped being returned by the API for long enough to be treated
    /// as withdrawn rather than a transient failure. Written copies are removed.
    /// </summary>
    Withdrawn,
}

/// <summary>What has happened to one subscriber's copy of one menu-day (ADR-150).</summary>
public enum MealDeliveryState
{
    /// <summary>Nothing is written yet, or the content moved and the copy needs a patch.</summary>
    Pending,

    /// <summary>The event exists on the subscriber's managed calendar at the applied version.</summary>
    Written,

    /// <summary>The subscriber could not receive it; <c>SkipReason</c> says why.</summary>
    Skipped,

    /// <summary>
    /// The event was written and has since been removed — the day was withdrawn or the subscriber
    /// turned the menu off. The row keeps the history.
    /// </summary>
    Removed,

    /// <summary>Writing or removing failed for a reason that is neither a skip nor transient.</summary>
    Failed,
}

/// <summary>
/// Why a subscriber cannot receive a menu-day (ADR-150). These are eligibility facts, not
/// preferences: there is no calendar to write to, so none of them can be waived. They mirror the
/// announcement exclusions (ADR-107) because the calendar preconditions are the same.
/// </summary>
public enum MealDeliveryExclusionReason
{
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
