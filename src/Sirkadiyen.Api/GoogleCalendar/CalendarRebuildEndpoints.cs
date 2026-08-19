using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.GoogleCalendar;

/// <summary>
/// Lets a student rebuild the dedicated calendar they deleted (ADR-116).
/// </summary>
/// <remarks>
/// This is the one door out of a dead end. A deleted calendar marks the connection unavailable,
/// which drops the student out of every writer and makes onboarding report <c>ActionRequired</c>;
/// that routes them to the consent screen, and re-consenting does not clear the flag, so they are
/// routed there again indefinitely. The endpoint lives beside the reconcile one because both are
/// the same kind of thing — a student asking for their own calendar to be put right — and it is
/// rate-limited by the same policy, since it is heavier still.
/// </remarks>
public static class CalendarRebuildEndpoints
{
    private static readonly JsonSerializerOptions AuditMetadataOptions = ContractJson.CreateOptions();

    public static IEndpointRouteBuilder MapCalendarRebuildEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/calendar/rebuild", AssessAsync)
            .RequireAuthorization()
            .WithTags("Calendar Sync")
            .WithSummary("Says whether the user's managed calendar needs rebuilding.");

        builder.MapPost("/api/calendar/rebuild", RequestAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingPolicies.CalendarReconcile)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithTags("Calendar Sync")
            .WithSummary("Rebuilds the user's own managed calendar after it was deleted.");

        return builder;
    }

    private static async Task<IResult> AssessAsync(
        ClaimsPrincipal principal,
        ManagedCalendarRebuildService rebuilds,
        CancellationToken cancellationToken) =>
        Results.Ok(await rebuilds.AssessAsync(
            UserClaimsPrincipalFactory.GetRequiredUserId(principal),
            cancellationToken));

    private static async Task<IResult> RequestAsync(
        ClaimsPrincipal principal,
        ManagedCalendarRebuildService rebuilds,
        AuditEventRecorder audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        ManagedCalendarRebuildResult result = await rebuilds.RequestAsync(
            userId,
            (assessment, token) => audit.RecordAsync(
                new AuditEventDraft
                {
                    Category = AuditEventCategory.ManagedCalendarRebuilt,
                    ActorUserId = userId,
                    ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                    SubjectType = "Calendar",
                    SubjectId = userId.ToString(),
                    CorrelationId = context.CorrelationId(),
                    ClientIp = context.ClientIp(),
                    UserAgent = context.ClientUserAgent(),
                    // No operator reason: the student is acting on their own account, and the
                    // fact that they asked is the reason. What matters for reconstruction later
                    // is how long the calendar had been gone.
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            requestedBy = "user",
                            unavailableSinceUtc = assessment.UnavailableSinceUtc,
                        },
                        AuditMetadataOptions),
                },
                token),
            cancellationToken);

        return CalendarRebuildResults.ToResult(result);
    }
}

/// <summary>
/// Turns a rebuild outcome into a response. Shared by the student's endpoint and the operator's so
/// the two doors cannot describe the same state differently.
/// </summary>
public static class CalendarRebuildResults
{
    public static IResult ToResult(ManagedCalendarRebuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            ManagedCalendarRebuildOutcome.Reset =>
                Results.Accepted("/api/calendar/sync", result),

            ManagedCalendarRebuildOutcome.NotEligible => Results.Problem(
                title: "There is nothing to rebuild",
                detail: "This calendar has not been found unreachable. A calendar you have only "
                    + "hidden from your list still exists, and reconciliation repairs it.",
                statusCode: StatusCodes.Status409Conflict),

            ManagedCalendarRebuildOutcome.Frozen => Results.Problem(
                title: "Operations are frozen",
                detail: "No calendar state may be discarded while a freeze is in force.",
                statusCode: StatusCodes.Status409Conflict),

            ManagedCalendarRebuildOutcome.NoConnection => Results.Problem(
                title: "No calendar to rebuild",
                detail: "Grant Sirkadiyen permission to manage its own calendar first.",
                statusCode: StatusCodes.Status409Conflict),

            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
