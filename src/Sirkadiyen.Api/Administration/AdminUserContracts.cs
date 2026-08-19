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

/// <summary>
/// Authorizes the re-check the operator was shown for one student (ADR-115).
/// </summary>
/// <remarks>
/// <see cref="PlanHash"/> binds the confirmation to that plan, and <see cref="Reason"/> is
/// recorded with it because a re-check queues calendar deletions no published revision derived —
/// the same requirement a cohort repair carries, at a smaller blast radius.
/// </remarks>
public sealed record RequestUserCalendarRecheck
{
    public required string PlanHash { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// An operator's request to rebuild a student's deleted managed calendar (ADR-116).
/// </summary>
/// <remarks>
/// Unlike the student's own endpoint this carries a reason, because the person deciding is not
/// the person whose event ledger is discarded.
/// </remarks>
public sealed record RequestManagedCalendarRebuild
{
    public required string Reason { get; init; }
}
