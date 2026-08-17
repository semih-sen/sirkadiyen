using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.Scheduling.Access;

namespace Sirkadiyen.Api.Administration;

public sealed record AdminUserDetailResponse
{
    public required AdminUserDetail User { get; init; }

    public required OnboardingState OnboardingState { get; init; }

    public required IReadOnlyList<AuditEventView> RecentSignIns { get; init; }

    /// <summary>
    /// The user's recent audit events across every category, so a profile change or a reconcile
    /// request is visible on the account itself rather than only in the audit screen.
    /// </summary>
    public required IReadOnlyList<AuditEventView> RecentActivity { get; init; }
}

/// <summary>
/// What is actually on a user's managed calendar over a local-date window, with the window the
/// server resolved echoed back — a caller that passed no dates must not have to guess which days it
/// is looking at.
/// </summary>
public sealed record AdminUserCalendarEventsResponse
{
    public required DateOnly FromLocalDate { get; init; }

    public required DateOnly ToLocalDate { get; init; }

    public required string TimeZoneId { get; init; }

    public required IReadOnlyList<UserScheduleEventView> Events { get; init; }
}
