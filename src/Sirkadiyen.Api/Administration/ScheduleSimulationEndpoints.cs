using Microsoft.AspNetCore.Mvc;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Api.StudentProfiles;
using Sirkadiyen.Application.Scheduling.Access;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// Renders one week of the live published schedule as an arbitrary cohort would receive it.
/// Nothing here writes, queues, or touches a calendar.
/// </summary>
public static class ScheduleSimulationEndpoints
{
    public static IEndpointRouteBuilder MapScheduleSimulationEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder simulation = builder
            .MapGroup("/api/admin/schedule-simulation")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Administration");

        simulation.MapGet("/week", GetWeekAsync)
            .WithSummary(
                "Resolves one week of the live published schedule for a stated cohort. Reads only.");

        return builder;
    }

    /// <summary>
    /// A GET, because it writes nothing: the preview/plan-hash/confirm convention belongs to the
    /// endpoints that go on to act. A plain URL is also pasteable into a bug report, which is
    /// most of what a diagnostic view is for.
    /// </summary>
    private static async Task<IResult> GetWeekAsync(
        CohortScheduleSimulationService simulation,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        int classYear,
        ProgramLanguage programLanguage,
        [FromQuery(Name = "selector")] string[]? selector = null,
        DateOnly? date = null)
    {
        if (!SelectorQueryReader.TryRead(
            selector,
            out Dictionary<string, string> selectors,
            out string? selectorProblem))
        {
            return Results.Problem(
                title: "Invalid cohort query",
                detail: selectorProblem,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // "Today" is the Istanbul local date, because the schedule is interpreted in that zone
        // and an operator elsewhere must still land on the same week (AI_GUIDELINE §16).
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(
            CohortScheduleSimulationService.ScheduleTimeZoneId);
        DateOnly anchor = date ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);

        try
        {
            CohortSimulationWeek week = await simulation.SimulateAsync(
                new CohortSimulationQuery
                {
                    ClassYear = classYear,
                    ProgramLanguage = programLanguage,
                    Selectors = selectors,
                    AnchorLocalDate = anchor,
                },
                cancellationToken);

            return Results.Ok(week);
        }
        catch (CohortSimulationValidationException exception)
        {
            // The same per-field shape a profile save returns, from the same validator, so the
            // operator reads one vocabulary of cohort errors across the panel (ADR-158).
            return Results.ValidationProblem(
                StudentProfileEndpoints.ToProblemErrors(exception.Errors));
        }
    }
}
