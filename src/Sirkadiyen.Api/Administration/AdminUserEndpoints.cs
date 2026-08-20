using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Sirkadiyen.Api.GoogleCalendar;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.Scheduling.Access;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// Read-only SuperAdmin views over user accounts: a filterable, sortable list, a per-user detail
/// that composes profile, license history, Calendar connection, onboarding state and recent audit
/// activity, and a read of what is actually on that user's managed calendar.
/// </summary>
/// <remarks>
/// Almost nothing here writes. The two user-scoped writes are the calendar re-check (ADR-115),
/// which queues a convergence and performs no calendar operation of its own, and the calendar
/// rebuild (ADR-116), which discards a ledger describing a calendar that no longer exists so the
/// student can start their synchronization again. Manual activation stays in
/// <c>LicenseEndpoints</c>, where the license service that performs it lives.
/// </remarks>
public static class AdminUserEndpoints
{
    private const int MaximumPageSize = 200;

    private const int RecentSignInCount = 10;

    private const int RecentAuditCount = 20;

    private const string ScheduleTimeZoneId = "Europe/Istanbul";

    private const int MaximumCalendarWindowDays = 400;

    private const int MaximumCalendarItems = 1000;

    private const int MaximumCalendarChangeItems = 100;

    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder users = builder
            .MapGroup("/api/admin/users")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("User Administration");

        users.MapGet("/", ListAsync)
            .WithSummary("Lists user accounts with identity, profile, license and calendar filters.");

        users.MapGet("/{userId:guid}", FindAsync)
            .WithSummary("Returns one user with profile, licenses, calendar state and recent activity.");

        users.MapGet("/{userId:guid}/calendar-events", ListCalendarEventsAsync)
            .WithSummary("Returns what the mapping ledger says is on this user's managed calendar.");

        users.MapGet("/{userId:guid}/calendar-changes", ListCalendarChangesAsync)
            .WithSummary("Returns the most recent creations and updates on this user's calendar.");

        // The read-only counterpart to the ledger views above: this one reads the actual Google
        // calendar and compares it with the ledger and current published truth (ADR-121), so an
        // operator can confirm what is really on the calendar rather than what Sirkadiyen recorded.
        users.MapGet("/{userId:guid}/calendar-verify", VerifyCalendarAsync)
            .WithSummary("Reads this user's Google calendar directly and reports drift from our records.");

        // A re-check is the cohort repair narrowed to one student (ADR-111, ADR-115). It lives
        // here rather than under /api/operations because the question it answers — "is this
        // person's calendar right?" — is asked while looking at that person.
        users.MapPost("/{userId:guid}/calendar-recheck/preview", PreviewCalendarRecheckAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Shows what re-synchronizing this one user's calendar would converge.");
        users.MapPost("/{userId:guid}/calendar-recheck", RequestCalendarRecheckAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Authorizes the shown re-check, with an audit entry.");

        // The operator's door to the same rebuild the student has (ADR-116), for the student who
        // does not find or cannot use theirs.
        users.MapGet("/{userId:guid}/calendar-rebuild", AssessCalendarRebuildAsync)
            .WithSummary("Says whether this user's managed calendar needs rebuilding.");
        users.MapPost("/{userId:guid}/calendar-rebuild", RequestCalendarRebuildAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Rebuilds this user's deleted managed calendar, with an audit entry.");

        // Permanently delete a student's account on their behalf (ADR-118). The same service the
        // student's own "Hesabımı sil" uses; here the actor is the operator and a reason is required.
        users.MapPost("/{userId:guid}/delete", DeleteUserAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Permanently deletes this user's account, with an audit entry.");

        // Promote a user to operator, or remove operator rights (ADR-119). Audited, guarded against
        // self-change and demoting the bootstrap operator.
        users.MapPost("/{userId:guid}/role", ChangeRoleAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Changes this user's authorization role, with an audit entry.");

        return builder;
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid userId,
        ChangeUserRoleRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        UserRoleService roles,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.Role))
        {
            return Results.Problem(
                title: "Invalid role change request",
                detail: "'role' must be 'User' or 'SuperAdmin'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > MaximumDeletionReasonLength)
        {
            return Results.Problem(
                title: "Invalid role change request",
                detail: $"'reason' is required and must contain at most "
                    + $"{MaximumDeletionReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Guid actorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        ChangeUserRoleServiceResult result = await roles.ChangeRoleAsync(
            new ChangeUserRoleCommand
            {
                TargetUserId = userId,
                ActorUserId = actorUserId,
                NewRole = request.Role,
            },
            (change, token) => audit.RecordAsync(
                new AuditEventDraft
                {
                    Category = AuditEventCategory.RoleChanged,
                    ActorUserId = actorUserId,
                    ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                    SubjectType = "User",
                    SubjectId = userId.ToString(),
                    CorrelationId = context.CorrelationId(),
                    ClientIp = context.ClientIp(),
                    UserAgent = context.ClientUserAgent(),
                    Reason = request.Reason.Trim(),
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            previousRole = change.PreviousRole.ToString(),
                            newRole = change.NewRole.ToString(),
                        },
                        AuditMetadataOptions),
                },
                token),
            cancellationToken);

        return result.Outcome switch
        {
            ChangeUserRoleServiceOutcome.Changed or ChangeUserRoleServiceOutcome.Unchanged =>
                Results.Ok(result),

            ChangeUserRoleServiceOutcome.CannotChangeOwnRole => Results.Problem(
                title: "You cannot change your own role",
                detail: "Ask another administrator to change your role.",
                statusCode: StatusCodes.Status409Conflict),

            ChangeUserRoleServiceOutcome.CannotDemoteBootstrapOperator => Results.Problem(
                title: "The bootstrap operator cannot be demoted",
                detail: "This account's operator role is re-granted on every sign-in and cannot be "
                    + "removed here.",
                statusCode: StatusCodes.Status409Conflict),

            _ => NotFound(userId),
        };
    }

    /// <summary>Bounds the operator's deletion reason, matching the calendar-repair reason bound.</summary>
    private const int MaximumDeletionReasonLength = 500;

    private static async Task<IResult> DeleteUserAsync(
        Guid userId,
        DeleteUserRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        AccountDeletionService deletion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > MaximumDeletionReasonLength)
        {
            return Results.Problem(
                title: "Invalid account deletion request",
                detail: $"'reason' is required and must contain at most "
                    + $"{MaximumDeletionReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        AccountDeletionResult result = await deletion.DeleteAsync(
            new AccountDeletionRequest
            {
                UserId = userId,
                RequestedByOperator = true,
                ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                ConfirmEmail = request.ConfirmEmail,
                Reason = request.Reason.Trim(),
                CorrelationId = context.CorrelationId(),
                ClientIp = context.ClientIp(),
                UserAgent = context.ClientUserAgent(),
            },
            cancellationToken);

        return result.Outcome switch
        {
            AccountDeletionOutcome.Deleted => Results.Ok(result),

            AccountDeletionOutcome.EmailMismatch => Results.Problem(
                title: "Confirmation does not match",
                detail: "The confirmation e-mail must match this account's e-mail exactly.",
                statusCode: StatusCodes.Status400BadRequest),

            AccountDeletionOutcome.SuperAdminRefused => Results.Problem(
                title: "Administrator account cannot be deleted",
                detail: "An administrator account cannot be deleted. Change the role first if this "
                    + "account must be removed.",
                statusCode: StatusCodes.Status409Conflict),

            _ => NotFound(userId),
        };
    }

    /// <summary>Matches the calendar-repair reason bound, so one operator note is not longer than another.</summary>
    private const int MaximumRecheckReasonLength = 500;

    private static readonly JsonSerializerOptions AuditMetadataOptions = ContractJson.CreateOptions();

    private static async Task<IResult> PreviewCalendarRecheckAsync(
        Guid userId,
        CohortCalendarRepairService repairs,
        CancellationToken cancellationToken)
    {
        CohortRepairPlan? plan = await repairs.PlanForUserAsync(userId, cancellationToken);
        return plan is null ? NotSynchronizable() : Results.Ok(plan);
    }

    private static async Task<IResult> RequestCalendarRecheckAsync(
        Guid userId,
        RequestUserCalendarRecheck request,
        ClaimsPrincipal principal,
        HttpContext context,
        CohortCalendarRepairService repairs,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.PlanHash))
        {
            return Results.Problem(
                title: "Invalid calendar re-check request",
                detail: "'planHash' is required: a re-check may only be confirmed against the "
                    + "plan it was shown for.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // A single-user re-check still queues deletions no published revision derived, so it
        // carries the same reason requirement the cohort repair does (AI_GUIDELINE §19).
        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > MaximumRecheckReasonLength)
        {
            return Results.Problem(
                title: "Invalid calendar re-check request",
                detail: $"'reason' is required and must contain at most "
                    + $"{MaximumRecheckReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        CohortRepairRequestResult result = await repairs.RequestForUserAsync(
            userId,
            request.PlanHash,
            (plan, token) => audit.RecordAsync(
                new AuditEventDraft
                {
                    Category = AuditEventCategory.CalendarRepairRequested,
                    ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                    ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                    // The subject is the student, not the cohort, so the entry appears on their
                    // own activity trail — which is where anyone asking why their calendar
                    // changed will look first.
                    SubjectType = "User",
                    SubjectId = userId.ToString(),
                    CorrelationId = context.CorrelationId(),
                    ClientIp = context.ClientIp(),
                    UserAgent = context.ClientUserAgent(),
                    Reason = request.Reason.Trim(),
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            planHash = plan.PlanHash,
                            scope = plan.Scope.ToString(),
                            surplus = plan.TotalSurplusEvents,
                            missing = plan.TotalMissingEvents,
                            retiredUntouched = plan.TotalUntouchableRetired,
                        },
                        AuditMetadataOptions),
                },
                token),
            cancellationToken);

        return result.Outcome switch
        {
            CohortRepairOutcome.Requested =>
                Results.Accepted($"/api/admin/users/{userId}/calendar-recheck", result),

            CohortRepairOutcome.PlanChanged => Results.Problem(
                title: "The re-check plan has changed",
                detail: "This user no longer resolves to the plan you confirmed. Review the new "
                    + "preview and confirm that one instead.",
                statusCode: StatusCodes.Status409Conflict),

            CohortRepairOutcome.Frozen => Results.Problem(
                title: "Operations are frozen",
                detail: "No calendar work may be queued while a global or scoped freeze is in "
                    + "force. Lift the freeze first.",
                statusCode: StatusCodes.Status409Conflict),

            // NothingToRepair with no plan is the "not synchronizable" case the service reports
            // by returning no plan at all; with one, it means the calendar already agrees.
            _ => result.Plan is null ? NotSynchronizable() : Results.Ok(result),
        };
    }

    /// <summary>
    /// A user with no synchronizable calendar has nothing to converge onto, which is a statement
    /// about their account rather than a server fault.
    /// </summary>
    private static IResult NotSynchronizable() =>
        Results.Problem(
            title: "This user has no synchronizable calendar",
            detail: "A re-check needs an authorized connection whose initial sync has completed, "
                + "a managed calendar that is still reachable, and an active licence.",
            statusCode: StatusCodes.Status409Conflict);

    private static async Task<IResult> AssessCalendarRebuildAsync(
        Guid userId,
        ManagedCalendarRebuildService rebuilds,
        CancellationToken cancellationToken) =>
        Results.Ok(await rebuilds.AssessAsync(userId, cancellationToken));

    private static async Task<IResult> RequestCalendarRebuildAsync(
        Guid userId,
        RequestManagedCalendarRebuild request,
        ClaimsPrincipal principal,
        HttpContext context,
        ManagedCalendarRebuildService rebuilds,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // An operator acting on someone else's account states why. The student's own endpoint does
        // not ask for one — that they asked is the reason — but here the account owner is not the
        // person making the decision, and they are the one who has to be able to find out later.
        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > MaximumRecheckReasonLength)
        {
            return Results.Problem(
                title: "Invalid calendar rebuild request",
                detail: $"'reason' is required and must contain at most "
                    + $"{MaximumRecheckReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        ManagedCalendarRebuildResult result = await rebuilds.RequestAsync(
            userId,
            (assessment, token) => audit.RecordAsync(
                new AuditEventDraft
                {
                    Category = AuditEventCategory.ManagedCalendarRebuilt,
                    ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                    ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                    // The subject is the student whose ledger is discarded, not the operator, so
                    // the entry lands on the trail the person affected would be shown.
                    SubjectType = "Calendar",
                    SubjectId = userId.ToString(),
                    CorrelationId = context.CorrelationId(),
                    ClientIp = context.ClientIp(),
                    UserAgent = context.ClientUserAgent(),
                    Reason = request.Reason.Trim(),
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            requestedBy = "operator",
                            unavailableSinceUtc = assessment.UnavailableSinceUtc,
                        },
                        AuditMetadataOptions),
                },
                token),
            cancellationToken);

        return CalendarRebuildResults.ToResult(result);
    }

    private static async Task<IResult> ListAsync(
        IAdminUserReadStore store,
        CancellationToken cancellationToken,
        string? search = null,
        UserRole? role = null,
        UserLicenseState? licenseState = null,
        bool? hasProfile = null,
        string? academicYear = null,
        int? classYear = null,
        ProgramLanguage? programLanguage = null,
        [FromQuery(Name = "selector")] string[]? selectors = null,
        bool? hasCalendarConnection = null,
        GoogleCalendarConnectionStatus? calendarStatus = null,
        GoogleCalendarInitialSyncState? initialSyncState = null,
        DateTimeOffset? createdFromUtc = null,
        DateTimeOffset? createdToUtc = null,
        DateTimeOffset? lastSignedInFromUtc = null,
        DateTimeOffset? lastSignedInToUtc = null,
        AdminUserSort sort = AdminUserSort.CreatedAtUtc,
        bool descending = true,
        int page = 1,
        int pageSize = 50)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            return Invalid($"'pageSize' must be between 1 and {MaximumPageSize}.");
        }

        if (!TryReadSelectors(selectors, out Dictionary<string, string> parsedSelectors, out string? selectorProblem))
        {
            return Invalid(selectorProblem!);
        }

        return Results.Ok(await store.ListAsync(
            new AdminUserQuery
            {
                Search = search,
                Role = role,
                LicenseState = licenseState,
                HasProfile = hasProfile,
                AcademicYear = academicYear,
                ClassYear = classYear,
                ProgramLanguage = programLanguage,
                Selectors = parsedSelectors,
                HasCalendarConnection = hasCalendarConnection,
                CalendarStatus = calendarStatus,
                InitialSyncState = initialSyncState,
                CreatedFromUtc = createdFromUtc,
                CreatedToUtc = createdToUtc,
                LastSignedInFromUtc = lastSignedInFromUtc,
                LastSignedInToUtc = lastSignedInToUtc,
                Sort = sort,
                Descending = descending,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken));
    }

    private static async Task<IResult> FindAsync(
        Guid userId,
        IAdminUserReadStore store,
        OnboardingStateService onboarding,
        IAuditEventStore auditStore,
        CancellationToken cancellationToken)
    {
        AdminUserDetail? detail = await store.FindAsync(userId, cancellationToken);
        if (detail is null)
        {
            return NotFound(userId);
        }

        OnboardingSnapshot onboardingState = await onboarding.GetAsync(userId, cancellationToken);
        var signIns = await auditStore.QueryAsync(
            new AuditEventQuery
            {
                Category = AuditEventCategory.SignIn,
                ActorUserId = userId,
                PageSize = RecentSignInCount,
            },
            cancellationToken);

        // Every category, so a profile change or a reconcile request the student made is visible
        // beside the sign-ins rather than only in the separate audit screen.
        var recentActivity = await auditStore.QueryAsync(
            new AuditEventQuery
            {
                ActorUserId = userId,
                PageSize = RecentAuditCount,
            },
            cancellationToken);

        return Results.Ok(new AdminUserDetailResponse
        {
            User = detail,
            OnboardingState = onboardingState.State,
            RecentSignIns = signIns.Items,
            RecentActivity = recentActivity.Items,
        });
    }

    /// <summary>
    /// The user's managed events over a local-date window, read from the mapping ledger — that is,
    /// what is actually on the calendar Sirkadiyen created for them, not what the published
    /// schedule says should be.
    /// </summary>
    private static async Task<IResult> ListCalendarEventsAsync(
        Guid userId,
        IAdminUserReadStore store,
        IUserScheduleReadStore scheduleStore,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 200)
    {
        if (limit is < 1 or > MaximumCalendarItems)
        {
            return Invalid($"'limit' must be between 1 and {MaximumCalendarItems}.");
        }

        if (await store.FindAsync(userId, cancellationToken) is null)
        {
            return NotFound(userId);
        }

        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(ScheduleTimeZoneId);
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);

        DateOnly fromDate = from ?? today;
        DateOnly toDate = to ?? fromDate.AddDays(30);

        if (toDate < fromDate)
        {
            return Invalid("'to' cannot be before 'from'.");
        }

        if (toDate.DayNumber - fromDate.DayNumber > MaximumCalendarWindowDays)
        {
            return Invalid($"The window may span at most {MaximumCalendarWindowDays} days.");
        }

        return Results.Ok(new AdminUserCalendarEventsResponse
        {
            FromLocalDate = fromDate,
            ToLocalDate = toDate,
            TimeZoneId = ScheduleTimeZoneId,
            Events = await scheduleStore.ListUpcomingAsync(
                userId,
                fromDate,
                toDate,
                limit,
                cancellationToken),
        });
    }

    /// <summary>
    /// Reads the user's actual Google calendar and compares it with the mapping ledger and current
    /// published truth (ADR-121). Read-only: it makes one live Google read and mutates nothing —
    /// neither the calendar, the ledger, nor the connection's health. Non-verifiable states (no
    /// connection, needs re-authorization, not yet synced) come back as a 200 with the outcome and a
    /// reason, so the panel can explain them rather than showing a bare error.
    /// </summary>
    private static async Task<IResult> VerifyCalendarAsync(
        Guid userId,
        IAdminUserReadStore store,
        CalendarVerificationService verification,
        CancellationToken cancellationToken)
    {
        if (await store.FindAsync(userId, cancellationToken) is null)
        {
            return NotFound(userId);
        }

        return Results.Ok(await verification.VerifyAsync(userId, cancellationToken));
    }

    private static async Task<IResult> ListCalendarChangesAsync(
        Guid userId,
        IAdminUserReadStore store,
        IUserScheduleReadStore scheduleStore,
        CancellationToken cancellationToken,
        int limit = 20)
    {
        if (limit is < 1 or > MaximumCalendarChangeItems)
        {
            return Invalid($"'limit' must be between 1 and {MaximumCalendarChangeItems}.");
        }

        if (await store.FindAsync(userId, cancellationToken) is null)
        {
            return NotFound(userId);
        }

        return Results.Ok(
            await scheduleStore.ListRecentChangesAsync(userId, limit, cancellationToken));
    }

    /// <summary>
    /// Reads repeated <c>?selector=key:value</c> parameters. A malformed pair is refused rather than
    /// skipped: silently dropping one would return a wider result set than the operator asked for
    /// and nothing on the screen would say so.
    /// </summary>
    private static bool TryReadSelectors(
        string[]? values,
        out Dictionary<string, string> selectors,
        out string? problem)
    {
        selectors = new Dictionary<string, string>(StringComparer.Ordinal);
        problem = null;

        foreach (string value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            int separator = value.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == value.Length - 1)
            {
                problem = $"'selector' must be written as 'key:value'; '{value}' is not.";
                return false;
            }

            selectors[value[..separator].Trim()] = value[(separator + 1)..].Trim();
        }

        return true;
    }

    private static IResult Invalid(string detail) => Results.Problem(
        title: "Invalid user query",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult NotFound(Guid userId) => Results.Problem(
        title: "User not found",
        detail: $"No user with ID '{userId}' exists.",
        statusCode: StatusCodes.Status404NotFound);
}
