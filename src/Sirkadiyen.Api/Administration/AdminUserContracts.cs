using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.Scheduling.Access;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Scheduling.Sources;

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
/// An operator's request to queue a non-destructive reconciliation of a student's calendar (ADR-123).
/// </summary>
/// <remarks>
/// It re-writes the events the ledger records but Google is missing and patches drifted ones, via the
/// worker's fenced inventory pass. <see cref="Reason"/> is recorded because an operator is acting on
/// someone else's calendar; no plan hash is needed because it derives no deletion.
/// </remarks>
public sealed record RepairUserCalendarRequest
{
    public required string Reason { get; init; }
}

public sealed record RepairUserCalendarResponse
{
    public required bool Requested { get; init; }
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

/// <summary>
/// An operator's request to permanently delete a student's account (ADR-118).
/// </summary>
/// <remarks>
/// <see cref="ConfirmEmail"/> must equal the target account's e-mail — the confirmation phrase that
/// makes an operator name the exact account being erased (§30) — and <see cref="Reason"/> is
/// recorded because the person deciding is not the account owner, and "why was my account deleted"
/// has to be answerable from the trail alone (AI_GUIDELINE §19).
/// </remarks>
public sealed record DeleteUserRequest
{
    public required string ConfirmEmail { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// An operator's request to change a user's authorization role (ADR-119).
/// </summary>
/// <remarks>
/// <see cref="Role"/> is the target role (<c>User</c> or <c>SuperAdmin</c>); <see cref="Reason"/> is
/// recorded because a role is authorization itself and who granted or removed it must be answerable
/// from the trail (AI_GUIDELINE §19).
/// </remarks>
public sealed record ChangeUserRoleRequest
{
    public required UserRole Role { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// An operator's request to create or replace a student's academic profile on their behalf
/// (ADR-158).
/// </summary>
/// <remarks>
/// It carries the same payload as the student's own <c>PUT /api/profile</c> so the identical
/// supported-schema validation and audience/resync path runs (ADR-096); the only additions are the
/// target user in the route and <see cref="Reason"/>. The reason is recorded because a profile
/// change can retire the events the previous audience received, and here the person deciding is not
/// the account owner — "why did my lessons change" has to be answerable from the trail alone
/// (AI_GUIDELINE §19).
/// </remarks>
public sealed record SaveUserProfileRequest
{
    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    public string? StudentNumber { get; init; }

    public IReadOnlyDictionary<string, string>? Selectors { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// The stored profile after an operator edit, and whether the change queued a calendar
/// re-synchronization (ADR-096). It mirrors what the student's own save reports, minus the
/// onboarding snapshot, which is not what an operator looking at one account is asking for.
/// </summary>
public sealed record SaveUserProfileResponse
{
    public required StudentProfileView Profile { get; init; }

    public required bool AudienceChanged { get; init; }

    public required bool CalendarResyncRequested { get; init; }
}
