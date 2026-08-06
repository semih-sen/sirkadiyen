using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Api.GoogleCalendar;

public sealed record CalendarSyncResponse
{
    public required GoogleCalendarConnectionView Connection { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}

public sealed record CalendarSyncStatusResponse
{
    public required GoogleCalendarInitialSyncState InitialSyncState { get; init; }

    public required bool HasManagedCalendar { get; init; }

    public required int MappedEventCount { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}

/// <summary>
/// Ledger-derived synchronization progress for the current user.
/// </summary>
/// <remarks>
/// These counts are projected from the durable event-mapping ledger, so they describe what is
/// actually on the calendar — not per-stage attempt outcomes. "Unchanged", "failed", and a total
/// applicable-record count are intentionally not reported because the ledger does not record them
/// (AI_GUIDELINE §9); surfacing them would require the sync services to persist per-run metrics.
/// </remarks>
public sealed record CalendarSyncProgressResponse
{
    public required GoogleCalendarInitialSyncState InitialSyncState { get; init; }

    public required bool HasManagedCalendar { get; init; }

    /// <summary>Total events currently on the user's managed calendar.</summary>
    public required int MappedEventCount { get; init; }

    /// <summary>Mapped events still at their first-written content (never patched since).</summary>
    public required int CreatedEventCount { get; init; }

    /// <summary>Mapped events patched at least once since they were first written.</summary>
    public required int UpdatedEventCount { get; init; }

    public DateTimeOffset? FirstWrittenAtUtc { get; init; }

    public DateTimeOffset? LastWrittenAtUtc { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}
