using System.Security.Claims;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Scheduling.Access;

namespace Sirkadiyen.Api.Scheduling.Access;

/// <summary>
/// Read-only views of a student's own timetable, projected from what is actually on their calendar
/// (the event-mapping ledger). Nothing here writes to a calendar or invents a schedule.
/// </summary>
public static class ScheduleEndpoints
{
    private const string ScheduleTimeZoneId = "Europe/Istanbul";

    private const int MaximumUpcomingDays = 90;

    private const int MaximumUpcomingItems = 500;

    private const int MaximumChangeItems = 100;

    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder schedule = builder
            .MapGroup("/api/schedule")
            .RequireAuthorization()
            .WithTags("Schedule");

        schedule.MapGet("/upcoming", GetUpcomingAsync)
            .WithSummary("Returns the student's upcoming managed events within a day window.");

        schedule.MapGet("/changes", GetChangesAsync)
            .WithSummary("Returns the most recent creations and updates on the student's calendar.");

        return builder;
    }

    private static async Task<IResult> GetUpcomingAsync(
        ClaimsPrincipal principal,
        IUserScheduleReadStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        int days = 14)
    {
        if (days is < 1 or > MaximumUpcomingDays)
        {
            return Results.Problem(
                title: "Invalid day window",
                detail: $"'days' must be between 1 and {MaximumUpcomingDays}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        // "Today" is the Istanbul local date, because the schedule is interpreted in that zone
        // (AI_GUIDELINE §16). The window is inclusive of both ends.
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(ScheduleTimeZoneId);
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);

        IReadOnlyList<UserScheduleEventView> events = await store.ListUpcomingAsync(
            userId,
            today,
            today.AddDays(days),
            MaximumUpcomingItems,
            cancellationToken);

        return Results.Ok(events);
    }

    private static async Task<IResult> GetChangesAsync(
        ClaimsPrincipal principal,
        IUserScheduleReadStore store,
        CancellationToken cancellationToken,
        int limit = 20)
    {
        if (limit is < 1 or > MaximumChangeItems)
        {
            return Results.Problem(
                title: "Invalid limit",
                detail: $"'limit' must be between 1 and {MaximumChangeItems}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        IReadOnlyList<UserScheduleChangeView> changes = await store.ListRecentChangesAsync(
            userId,
            limit,
            cancellationToken);

        return Results.Ok(changes);
    }
}
