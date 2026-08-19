using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.Operations;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.Operations;

/// <summary>Administrative operational state.</summary>
public static class OperationalEndpoints
{
    /// <summary>Matches the freeze reason bound, so one operator note is not longer than another.</summary>
    private const int MaximumRepairReasonLength = OperationalFreezeControl.MaximumReasonLength;

    private static readonly JsonSerializerOptions AuditMetadataOptions = ContractJson.CreateOptions();

    public static IEndpointRouteBuilder MapOperationalEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder operations = builder
            .MapGroup("/api/operations")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Operations");

        operations.MapGet("/freeze", GetFreezeAsync)
            .WithSummary("Returns the runtime global operational freeze state.");
        operations.MapPost("/freeze", SetFreezeAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Freezes or unfreezes mutating pipelines with an audit entry.");

        operations.MapPost("/calendar-repairs/preview", PreviewCalendarRepairAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Shows what repairing one cohort's calendars would converge.");
        operations.MapPost("/calendar-repairs", RequestCalendarRepairAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Authorizes the shown cohort calendar repair, with an audit entry.");

        operations.MapPost("/profile-rollovers/preview", PreviewProfileRolloverAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Shows what moving a program's profiles to its sources' year would do.");
        operations.MapPost("/profile-rollovers", RequestProfileRolloverAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Authorizes the shown academic-year rollover, with an audit entry.");

        operations.MapGet("/freeze/scopes", ListScopedFreezesAsync)
            .WithSummary("Lists class-year/program-language operational freeze controls.");
        operations.MapPost("/freeze/scopes", SetScopedFreezeAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Freezes or unfreezes one class-year/program-language pipeline.");

        return builder;
    }

    private static Task<OperationalFreezeSnapshot> GetFreezeAsync(
        IOperationalFreezeStore store,
        CancellationToken cancellationToken) => store.GetAsync(cancellationToken);

    private static Task<IReadOnlyList<OperationalFreezeSnapshot>> ListScopedFreezesAsync(
        IOperationalFreezeStore store,
        CancellationToken cancellationToken) => store.ListScopedAsync(cancellationToken);

    private static async Task<IResult> SetFreezeAsync(
        SetOperationalFreezeRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOperationalFreezeStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > OperationalFreezeControl.MaximumReasonLength)
        {
            return Results.Problem(
                title: "Invalid operational freeze request",
                detail: $"'reason' is required and must contain at most "
                    + $"{OperationalFreezeControl.MaximumReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string correlationId = Activity.Current?.TraceId.ToString()
            ?? context.TraceIdentifier;
        OperationalFreezeChangeResult result = await store.SetAsync(
            request.IsFrozen,
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            request.Reason,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> PreviewCalendarRepairAsync(
        PreviewCalendarRepairRequest request,
        CohortCalendarRepairService repairs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Scope(request.AcademicYear, request.ClassYear, request.ProgramLanguage)
            is not { } scope)
        {
            return InvalidScope();
        }

        return Results.Ok(await repairs.PlanAsync(scope, cancellationToken));
    }

    private static async Task<IResult> RequestCalendarRepairAsync(
        RequestCalendarRepairRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        CohortCalendarRepairService repairs,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Scope(request.AcademicYear, request.ClassYear, request.ProgramLanguage)
            is not { } scope)
        {
            return InvalidScope();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHash))
        {
            return Results.Problem(
                title: "Invalid calendar repair request",
                detail: "'planHash' is required: a repair may only be confirmed against the plan "
                    + "it was shown for.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The reason is required for the same purpose it is on a freeze: this queues deletions no
        // published revision derived, and "why did these lessons disappear" must be answerable
        // from the audit trail alone (AI_GUIDELINE §19).
        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > MaximumRepairReasonLength)
        {
            return Results.Problem(
                title: "Invalid calendar repair request",
                detail: $"'reason' is required and must contain at most "
                    + $"{MaximumRepairReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The audit is written by the service immediately before it flags anything, so a failure
        // to record leaves the repair unqueued rather than untraceable.
        CohortRepairRequestResult result = await repairs.RequestAsync(
            scope,
            request.PlanHash,
            (plan, token) => audit.RecordAsync(
                new AuditEventDraft
                {
                    Category = AuditEventCategory.CalendarRepairRequested,
                    ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                    ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                    SubjectType = "CalendarRepair",
                    SubjectId = scope.ToString(),
                    CorrelationId = context.CorrelationId(),
                    ClientIp = context.ClientIp(),
                    UserAgent = context.ClientUserAgent(),
                    Reason = request.Reason.Trim(),
                    // The plan hash is recorded so the trail states exactly which plan was
                    // authorized, not merely that some repair of this cohort was. The counts are
                    // the authorized plan's, which is what the operator agreed to; how many
                    // connections could actually take the flag is reported in the response.
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            planHash = plan.PlanHash,
                            users = plan.Users.Count,
                            surplus = plan.TotalSurplusEvents,
                            missing = plan.TotalMissingEvents,
                            retiredUntouched = plan.TotalUntouchableRetired,
                        },
                        AuditMetadataOptions),
                },
                token),
            cancellationToken);

        switch (result.Outcome)
        {
            case CohortRepairOutcome.Requested:
                return Results.Accepted("/api/operations/calendar-repairs", result);

            case CohortRepairOutcome.PlanChanged:
                return Results.Problem(
                    title: "The repair plan has changed",
                    detail: "The cohort no longer resolves to the plan you confirmed. Review the "
                        + "new preview and confirm that one instead.",
                    statusCode: StatusCodes.Status409Conflict);

            case CohortRepairOutcome.Frozen:
                return Results.Problem(
                    title: "Operations are frozen",
                    detail: "No calendar work may be queued while a global or scoped freeze is in "
                        + "force. Lift the freeze first.",
                    statusCode: StatusCodes.Status409Conflict);

            case CohortRepairOutcome.NothingToRepair:
            default:
                return Results.Ok(result);
        }
    }

    private static async Task<IResult> PreviewProfileRolloverAsync(
        PreviewProfileRolloverRequest request,
        ProfileAcademicYearRolloverService rollovers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (RolloverScope(request.FromAcademicYear, request.ClassYear, request.ProgramLanguage)
            is not { } scope)
        {
            return InvalidRolloverScope();
        }

        return Results.Ok(await rollovers.PlanAsync(scope, cancellationToken));
    }

    private static async Task<IResult> RequestProfileRolloverAsync(
        RequestProfileRolloverRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        ProfileAcademicYearRolloverService rollovers,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (RolloverScope(request.FromAcademicYear, request.ClassYear, request.ProgramLanguage)
            is not { } scope)
        {
            return InvalidRolloverScope();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHash))
        {
            return Results.Problem(
                title: "Invalid profile rollover request",
                detail: "'planHash' is required: a rollover may only be confirmed against the "
                    + "plan it was shown for.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Required for the same purpose it is on a repair: this rewrites data students entered
        // about themselves, and "why did my profile change year" must be answerable from the
        // audit trail alone (AI_GUIDELINE §19).
        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > MaximumRepairReasonLength)
        {
            return Results.Problem(
                title: "Invalid profile rollover request",
                detail: $"'reason' is required and must contain at most "
                    + $"{MaximumRepairReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        ProfileRolloverRequestResult result = await rollovers.RequestAsync(
            scope,
            request.PlanHash,
            (plan, token) => audit.RecordAsync(
                new AuditEventDraft
                {
                    Category = AuditEventCategory.ProfileAcademicYearRolled,
                    ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                    ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                    SubjectType = "ProfileAcademicYearRollover",
                    SubjectId = scope.ToString(),
                    CorrelationId = context.CorrelationId(),
                    ClientIp = context.ClientIp(),
                    UserAgent = context.ClientUserAgent(),
                    Reason = request.Reason.Trim(),
                    // Both years are recorded, not only the target: the trail has to state what a
                    // profile was moved *from* for anyone later reconstructing which students
                    // were on which year when a given revision published.
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            planHash = plan.PlanHash,
                            fromAcademicYear = scope.FromAcademicYear,
                            toAcademicYear = plan.ToAcademicYear,
                            toSchemaVersion = plan.ToSchemaVersion,
                            profiles = plan.Users.Count,
                            gainedEvents = plan.TotalGainedEvents,
                            strandedEvents = plan.TotalStrandedEvents,
                            withoutConnection = plan.ProfilesWithoutSyncReadyConnection,
                            blocked = plan.BlockedByInvalidSelectors.Count,
                        },
                        AuditMetadataOptions),
                },
                token),
            cancellationToken);

        switch (result.Outcome)
        {
            case ProfileRolloverOutcome.Moved:
                return Results.Accepted("/api/operations/profile-rollovers", result);

            case ProfileRolloverOutcome.PlanChanged:
                return Results.Problem(
                    title: "The rollover plan has changed",
                    detail: "The program no longer resolves to the plan you confirmed. Review the "
                        + "new preview and confirm that one instead.",
                    statusCode: StatusCodes.Status409Conflict);

            case ProfileRolloverOutcome.Frozen:
                return Results.Problem(
                    title: "Operations are frozen",
                    detail: "No calendar work may be queued while a global or scoped freeze is in "
                        + "force. Lift the freeze first.",
                    statusCode: StatusCodes.Status409Conflict);

            case ProfileRolloverOutcome.NotSupportedBySchema:
                return Results.Problem(
                    title: "The deployed schema does not support this rollover",
                    detail: result.Refusal,
                    statusCode: StatusCodes.Status409Conflict);

            case ProfileRolloverOutcome.NothingToMove:
            default:
                return Results.Ok(result);
        }
    }

    private static ProfileRolloverScope? RolloverScope(
        string fromAcademicYear,
        int classYear,
        ProgramLanguage programLanguage) =>
        string.IsNullOrWhiteSpace(fromAcademicYear)
            || classYear is < 1 or > 6
            || !Enum.IsDefined(programLanguage)
                ? null
                : new ProfileRolloverScope
                {
                    FromAcademicYear = fromAcademicYear.Trim(),
                    ClassYear = classYear,
                    ProgramLanguage = programLanguage,
                };

    private static IResult InvalidRolloverScope() =>
        Results.Problem(
            title: "Invalid profile rollover scope",
            detail: "'fromAcademicYear' is required, 'classYear' must be between 1 and 6, and "
                + "'programLanguage' must be supported.",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>The scope a request names, or <see langword="null"/> when it is not a real one.</summary>
    private static CohortRepairScope? Scope(
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage) =>
        string.IsNullOrWhiteSpace(academicYear)
            || classYear is < 1 or > 6
            || !Enum.IsDefined(programLanguage)
                ? null
                : new CohortRepairScope
                {
                    AcademicYear = academicYear.Trim(),
                    ClassYear = classYear,
                    ProgramLanguage = programLanguage,
                };

    private static IResult InvalidScope() =>
        Results.Problem(
            title: "Invalid calendar repair scope",
            detail: "'academicYear' is required, 'classYear' must be between 1 and 6, and "
                + "'programLanguage' must be supported.",
            statusCode: StatusCodes.Status400BadRequest);

    private static async Task<IResult> SetScopedFreezeAsync(
        SetScopedOperationalFreezeRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOperationalFreezeStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ClassYear is < 1 or > 6 || !Enum.IsDefined(request.ProgramLanguage))
        {
            return Results.Problem(
                title: "Invalid operational freeze scope",
                detail: "'classYear' must be between 1 and 6 and 'programLanguage' must be supported.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > OperationalFreezeControl.MaximumReasonLength)
        {
            return Results.Problem(
                title: "Invalid operational freeze request",
                detail: $"'reason' is required and must contain at most {OperationalFreezeControl.MaximumReasonLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string correlationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        OperationalFreezeChangeResult result = await store.SetScopedAsync(
            new OperationalFreezeScope
            {
                ClassYear = request.ClassYear,
                ProgramLanguage = request.ProgramLanguage,
            },
            request.IsFrozen,
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            request.Reason,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return Results.Ok(result);
    }
}
