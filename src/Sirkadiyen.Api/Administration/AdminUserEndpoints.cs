using Microsoft.AspNetCore.Mvc;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Application.Scheduling.Access;
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
/// Nothing here writes. The one user-scoped write an operator has — manual activation — stays in
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

        return builder;
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
