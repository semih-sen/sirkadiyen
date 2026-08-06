using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.GoogleCalendar;

/// <summary>
/// Lets a student ask for their own calendar to be reconciled (ADR-062). The request only records
/// intent and schedules the worker's next non-destructive inventory pass sooner; it never mutates a
/// calendar directly and never derives a deletion.
/// </summary>
public static class CalendarReconcileEndpoints
{
    public static IEndpointRouteBuilder MapCalendarReconcileEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapPost("/api/calendar/reconcile", RequestAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingPolicies.CalendarReconcile)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithTags("Calendar Sync")
            .WithSummary("Requests a non-destructive reconciliation of the user's own calendar.");

        return builder;
    }

    private static async Task<IResult> RequestAsync(
        ClaimsPrincipal principal,
        IUserCalendarConnectionStore connectionStore,
        AuditEventRecorder audit,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Guid userId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);

        RequestReconciliationOutcome outcome = await connectionStore.RequestReconciliationAsync(
            userId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        switch (outcome)
        {
            case RequestReconciliationOutcome.Requested:
                await audit.RecordAsync(
                    new AuditEventDraft
                    {
                        Category = AuditEventCategory.ReconcileRequested,
                        ActorUserId = userId,
                        ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                        SubjectType = "Calendar",
                        SubjectId = userId.ToString(),
                        CorrelationId = context.CorrelationId(),
                        ClientIp = context.ClientIp(),
                        UserAgent = context.ClientUserAgent(),
                    },
                    cancellationToken);
                return Results.Accepted(
                    "/api/calendar/sync",
                    new RequestReconciliationResponse { Requested = true });

            case RequestReconciliationOutcome.NotEligible:
                return Results.Problem(
                    title: "Reconciliation is not available yet",
                    detail: "Your calendar can be reconciled only after the initial synchronization "
                        + "has completed and while the connection is healthy. Finish onboarding or "
                        + "re-authorize Calendar access first.",
                    statusCode: StatusCodes.Status409Conflict);

            case RequestReconciliationOutcome.NotFound:
            default:
                return Results.Problem(
                    title: "No calendar to reconcile",
                    detail: "Grant Sirkadiyen permission to manage its own calendar first.",
                    statusCode: StatusCodes.Status409Conflict);
        }
    }
}
