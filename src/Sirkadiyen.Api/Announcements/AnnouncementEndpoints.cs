using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.Announcements;

/// <summary>
/// The administrator surface for calendar announcements — the bulk cohort event and the
/// single-user warning, which are one domain behind two screens (ADR-107).
/// </summary>
/// <remarks>
/// Every route is SuperAdmin-only and every mutation is antiforgery-protected. The audience is
/// resolved and the plan is hashed on the server; the browser only carries the hash back, so it can
/// neither choose recipients nor confirm a plan the server did not compute (AI_GUIDELINE §6).
/// </remarks>
public static class AnnouncementEndpoints
{
    private const int MaximumListLimit = 200;

    private const int MaximumDeliveryPageSize = 200;

    private const int MaximumSelectorCount = 12;

    private const int MaximumSelectorPartLength = 100;

    public static IEndpointRouteBuilder MapAnnouncementEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder announcements = builder
            .MapGroup("/api/admin/announcements")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Announcements");

        announcements.MapGet("/options", GetOptions)
            .WithSummary("Returns the categories, warning templates and time rules a composer needs.");

        announcements.MapPost("/preview", PreviewAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Resolves the audience and returns the binding plan to confirm against.");

        announcements.MapPost("/", CreateAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Confirms an announcement, freezing its recipients and queueing delivery.");

        announcements.MapGet("/", ListAsync)
            .WithSummary("Lists announcements, newest first.");

        announcements.MapGet("/{id:guid}", FindAsync)
            .WithSummary("Returns one announcement with its content and exclusion breakdown.");

        announcements.MapGet("/{id:guid}/deliveries", ListDeliveriesAsync)
            .WithSummary("Returns the per-recipient delivery ledger.");

        announcements.MapPut("/{id:guid}", UpdateAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Corrects what an announcement says; every written copy is patched.");

        announcements.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Removes every copy already written to a calendar.");

        return builder;
    }

    private static IResult GetOptions(AnnouncementService service) =>
        Results.Ok(new AnnouncementCompositionOptions
        {
            Categories =
                [.. AnnouncementCategoryCatalog.List().Select(AnnouncementCategoryView.From)],
            Templates =
                [.. AnnouncementTemplateCatalog.List().Select(AnnouncementTemplateView.From)],
            TimeZoneId = AnnouncementService.TimeZoneId,
            EarliestLocalDate = service.EarliestLocalDate(),
        });

    private static async Task<IResult> PreviewAsync(
        CreateAnnouncementRequest request,
        AnnouncementService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryBuild(request.Announcement, service, out AnnouncementRequest? composed, out IResult? problem))
        {
            return problem!;
        }

        return Results.Ok(await service.PreviewAsync(composed!, cancellationToken));
    }

    private static async Task<IResult> CreateAsync(
        CreateAnnouncementRequest request,
        ClaimsPrincipal principal,
        AnnouncementService service,
        AuditEventRecorder audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryBuild(request.Announcement, service, out AnnouncementRequest? composed, out IResult? problem))
        {
            return problem!;
        }

        string reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Invalid("'reason' is required; it is written to the audit trail.");
        }

        if (string.IsNullOrWhiteSpace(request.PlanHash))
        {
            return Invalid("'planHash' is required. Preview the announcement first.");
        }

        Guid actorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        string actorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal);

        CreateAnnouncementResult result = await service.CreateAsync(
            composed!,
            request.PlanHash,
            request.ConfirmationPhrase ?? string.Empty,
            reason,
            actorUserId,
            actorEmail,
            cancellationToken);

        switch (result.Outcome)
        {
            case CreateAnnouncementOutcome.Queued:
                await audit.RecordAsync(
                    Draft(
                        AuditEventCategory.AnnouncementQueued,
                        result.Announcement!,
                        actorUserId,
                        actorEmail,
                        reason,
                        context,
                        composed!),
                    cancellationToken);
                return Results.Ok(result);

            case CreateAnnouncementOutcome.AlreadyExists:
                // A replay is a success with nothing to do, not a conflict to resolve: the same
                // announcement is already on its way to the same people (plan §4.4).
                return Results.Ok(result);

            case CreateAnnouncementOutcome.PlanChangedSincePreview:
                return Results.Problem(
                    title: "The plan changed since it was previewed",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status409Conflict);

            case CreateAnnouncementOutcome.ConfirmationMismatch:
                return Results.Problem(
                    title: "Confirmation phrase did not match",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status400BadRequest);

            case CreateAnnouncementOutcome.NoRecipients:
                return Results.Problem(
                    title: "No recipients",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status409Conflict);

            default:
                return Invalid(result.Detail ?? "The announcement request is not valid.");
        }
    }

    private static async Task<IResult> ListAsync(
        AnnouncementService service,
        CancellationToken cancellationToken,
        CalendarAnnouncementKind? kind = null,
        CalendarAnnouncementStatus? status = null,
        int limit = 50)
    {
        if (limit is < 1 or > MaximumListLimit)
        {
            return Invalid($"'limit' must be between 1 and {MaximumListLimit}.");
        }

        return Results.Ok(await service.ListAsync(kind, status, limit, cancellationToken));
    }

    private static async Task<IResult> FindAsync(
        Guid id,
        AnnouncementService service,
        CancellationToken cancellationToken) =>
        await service.FindAsync(id, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : NotFound(id);

    private static async Task<IResult> ListDeliveriesAsync(
        Guid id,
        AnnouncementService service,
        CancellationToken cancellationToken,
        CalendarAnnouncementDeliveryState? state = null,
        int page = 1,
        int pageSize = 50)
    {
        if (page < 1)
        {
            return Invalid("'page' starts at 1.");
        }

        if (pageSize is < 1 or > MaximumDeliveryPageSize)
        {
            return Invalid($"'pageSize' must be between 1 and {MaximumDeliveryPageSize}.");
        }

        PagedResult<AnnouncementDeliveryView> deliveries =
            await service.ListDeliveriesAsync(id, state, page, pageSize, cancellationToken);
        return Results.Ok(deliveries);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateAnnouncementRequest request,
        ClaimsPrincipal principal,
        AnnouncementService service,
        AuditEventRecorder audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryBuild(request.Announcement, service, out AnnouncementRequest? composed, out IResult? problem))
        {
            return problem!;
        }

        string reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Invalid("'reason' is required; it is written to the audit trail.");
        }

        Guid actorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        string actorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal);

        UpdateAnnouncementResult result = await service.UpdateAsync(
            id,
            composed!,
            actorEmail,
            reason,
            cancellationToken);

        switch (result.Outcome)
        {
            case UpdateAnnouncementOutcome.Updated:
                await audit.RecordAsync(
                    Draft(
                        AuditEventCategory.AnnouncementUpdated,
                        result.Announcement!,
                        actorUserId,
                        actorEmail,
                        reason,
                        context,
                        composed!),
                    cancellationToken);
                return Results.Ok(result);

            case UpdateAnnouncementOutcome.NotFound:
                return NotFound(id);

            case UpdateAnnouncementOutcome.Cancelled:
                return Results.Problem(
                    title: "The announcement is cancelled",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status409Conflict);

            case UpdateAnnouncementOutcome.ConcurrentChange:
                return Results.Problem(
                    title: "The announcement changed during the edit",
                    detail: "Another operator changed it. Read it again before editing.",
                    statusCode: StatusCodes.Status409Conflict);

            default:
                return Invalid(result.Detail ?? "The announcement request is not valid.");
        }
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        CancelAnnouncementRequest request,
        ClaimsPrincipal principal,
        AnnouncementService service,
        AuditEventRecorder audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            // Cancelling deletes events from student calendars, so it is the one action here that
            // could never be reconstructed from the announcement alone.
            return Invalid("'reason' is required; cancelling removes events from calendars.");
        }

        Guid actorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        string actorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal);

        CancelAnnouncementResult result =
            await service.CancelAsync(id, actorEmail, reason, cancellationToken);

        switch (result.Outcome)
        {
            case CancelAnnouncementOutcome.CancellationRequested:
                await audit.RecordAsync(
                    new AuditEventDraft
                    {
                        Category = AuditEventCategory.AnnouncementCancelled,
                        ActorUserId = actorUserId,
                        ActorEmail = actorEmail,
                        SubjectType = "CalendarAnnouncement",
                        SubjectId = id.ToString(),
                        CorrelationId = context.CorrelationId(),
                        ClientIp = context.ClientIp(),
                        UserAgent = context.ClientUserAgent(),
                        Reason = reason,
                        Metadata = JsonSerializer.Serialize(new
                        {
                            campaignKey = result.Announcement!.CampaignKey,
                            writtenCopies = result.Announcement.Counts.Written,
                        }),
                    },
                    cancellationToken);
                return Results.Ok(result);

            case CancelAnnouncementOutcome.AlreadyCancelled:
                return Results.Ok(result);

            case CancelAnnouncementOutcome.NotFound:
                return NotFound(id);

            default:
                return Results.Problem(
                    title: "The announcement changed during cancellation",
                    detail: "Another operator or the delivery worker changed it. Read it again.",
                    statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <remarks>
    /// The recipient list is deliberately not recorded. The delivery ledger already holds every
    /// recipient with their outcome, and copying hundreds of accounts into an audit row would
    /// duplicate personal data into a table nothing prunes (AI_GUIDELINE §15).
    /// </remarks>
    private static AuditEventDraft Draft(
        AuditEventCategory category,
        AnnouncementSummary announcement,
        Guid actorUserId,
        string actorEmail,
        string reason,
        HttpContext context,
        AnnouncementRequest composed) => new()
        {
            Category = category,
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            SubjectType = "CalendarAnnouncement",
            SubjectId = announcement.AnnouncementId.ToString(),
            CorrelationId = context.CorrelationId(),
            ClientIp = context.ClientIp(),
            UserAgent = context.ClientUserAgent(),
            Reason = reason,
            Metadata = JsonSerializer.Serialize(new
            {
                kind = announcement.Kind.ToString(),
                campaignKey = announcement.CampaignKey,
                contentVersion = announcement.ContentVersion,
                recipientCount = announcement.RecipientCount,
                localDate = announcement.LocalDate.ToString("yyyy-MM-dd"),
                academicYear = composed.Criteria.AcademicYear,
                classYear = composed.Criteria.ClassYear,
                programLanguage = composed.Criteria.ProgramLanguage?.ToString(),
                selectors = composed.Criteria.Selectors,
                targetUserId = composed.Criteria.TargetUserId,
            }),
        };

    /// <summary>
    /// Turns the browser's composition into the application request, or into the problem detail
    /// explaining why it is not one. Validated here so a malformed request is a 400 rather than an
    /// unhandled domain exception on its way to the database (AI_GUIDELINE §16).
    /// </summary>
    private static bool TryBuild(
        AnnouncementCompositionRequest? request,
        AnnouncementService service,
        out AnnouncementRequest? composed,
        out IResult? problem)
    {
        composed = null;
        problem = null;

        if (request is null)
        {
            problem = Invalid("'announcement' is required.");
            return false;
        }

        string title = request.Title?.Trim() ?? string.Empty;
        string body = request.Body?.Trim() ?? string.Empty;
        if (title.Length == 0 || title.Length > CalendarAnnouncement.MaximumTitleLength)
        {
            problem = Invalid(
                $"'title' is required and allows {CalendarAnnouncement.MaximumTitleLength} characters.");
            return false;
        }

        if (body.Length == 0 || body.Length > CalendarAnnouncement.MaximumBodyLength)
        {
            problem = Invalid(
                $"'body' is required and allows {CalendarAnnouncement.MaximumBodyLength} characters.");
            return false;
        }

        if ((request.Location?.Trim().Length ?? 0) > CalendarAnnouncement.MaximumLocationLength
            || (request.InternalNote?.Trim().Length ?? 0)
                > CalendarAnnouncement.MaximumInternalNoteLength)
        {
            problem = Invalid("'location' or 'internalNote' is too long.");
            return false;
        }

        string categoryKey = request.CategoryKey?.Trim() is { Length: > 0 } key
            ? key
            : AnnouncementCategoryCatalog.DefaultKey;
        if (!AnnouncementCategoryCatalog.IsKnown(categoryKey))
        {
            problem = Invalid($"'{categoryKey}' is not a known announcement category.");
            return false;
        }

        if (request.LocalDate is not { } localDate)
        {
            problem = Invalid("'localDate' is required.");
            return false;
        }

        DateOnly earliest = service.EarliestLocalDate();
        if (localDate < earliest)
        {
            // A calendar event in the past reaches nobody's attention, so accepting one would be
            // recording a delivery that cannot do what the operator intended.
            problem = Invalid(
                $"'localDate' cannot be before {earliest:yyyy-MM-dd} (Europe/Istanbul).");
            return false;
        }

        if (request.IsAllDay)
        {
            if (request.StartLocalTime is not null || request.EndLocalTime is not null)
            {
                problem = Invalid("An all-day announcement carries no times at all.");
                return false;
            }
        }
        else if (request.StartLocalTime is not { } start || request.EndLocalTime is not { } end)
        {
            problem = Invalid("A timed announcement needs both 'startLocalTime' and 'endLocalTime'.");
            return false;
        }
        else if (end <= start)
        {
            problem = Invalid("'endLocalTime' must be after 'startLocalTime'.");
            return false;
        }

        if (request.ReminderMinutesBefore is { } reminder
            && (reminder < 0 || reminder > CalendarAnnouncement.MaximumReminderMinutes))
        {
            problem = Invalid(
                $"'reminderMinutesBefore' must be between 0 and "
                + $"{CalendarAnnouncement.MaximumReminderMinutes}.");
            return false;
        }

        Dictionary<string, string> selectors = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> selector in request.Selectors ?? [])
        {
            if (string.IsNullOrWhiteSpace(selector.Key) || string.IsNullOrWhiteSpace(selector.Value))
            {
                continue;
            }

            if (selector.Key.Length > MaximumSelectorPartLength
                || selector.Value.Length > MaximumSelectorPartLength)
            {
                problem = Invalid("A selector name or value is too long.");
                return false;
            }

            selectors[selector.Key.Trim()] = selector.Value.Trim();
        }

        if (selectors.Count > MaximumSelectorCount)
        {
            problem = Invalid($"At most {MaximumSelectorCount} selectors are allowed.");
            return false;
        }

        string? templateKey = request.TemplateKey?.Trim() is { Length: > 0 } template
            ? template
            : null;

        if (request.Kind is CalendarAnnouncementKind.UserWarning)
        {
            if (request.TargetUserId is not { } target || target == Guid.Empty)
            {
                problem = Invalid("A warning needs 'targetUserId'.");
                return false;
            }

            if (templateKey is not null && AnnouncementTemplateCatalog.Find(templateKey) is null)
            {
                problem = Invalid($"'{templateKey}' is not a known warning template.");
                return false;
            }
        }
        else
        {
            if (request.TargetUserId is not null)
            {
                problem = Invalid(
                    "A bulk announcement is addressed to a cohort; use the warning flow to write "
                    + "to one user.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.AcademicYear))
            {
                problem = Invalid("'academicYear' is required for a bulk announcement.");
                return false;
            }

            // A template belongs to the warning flow; silently accepting one here would put a key
            // into the campaign identity that the bulk derivation never reads.
            templateKey = null;
        }

        composed = new AnnouncementRequest
        {
            Kind = request.Kind,
            Criteria = new AnnouncementAudienceCriteria
            {
                AcademicYear = request.AcademicYear?.Trim() ?? string.Empty,
                ClassYear = request.ClassYear,
                ProgramLanguage = request.ProgramLanguage,
                Selectors = selectors,
                TargetUserId = request.TargetUserId,
            },
            TemplateKey = templateKey,
            Title = title,
            Body = body,
            Location = request.Location?.Trim(),
            IsAllDay = request.IsAllDay,
            LocalDate = localDate,
            StartLocalTime = request.StartLocalTime,
            EndLocalTime = request.EndLocalTime,
            ReminderMinutesBefore = request.ReminderMinutesBefore,
            CategoryKey = categoryKey,
            InternalNote = request.InternalNote?.Trim(),
        };
        return true;
    }

    private static IResult Invalid(string detail) => Results.Problem(
        title: "Invalid announcement request",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult NotFound(Guid id) => Results.Problem(
        title: "Announcement not found",
        detail: $"No announcement with ID '{id}' exists.",
        statusCode: StatusCodes.Status404NotFound);
}
