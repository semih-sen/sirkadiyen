using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// The SuperAdmin surface for reading and editing the schedule source catalog document (ADR-114).
/// </summary>
/// <remarks>
/// The catalog says which document belongs to which program and which parser reads it, so an edit
/// here can retarget a whole cohort's lessons without any parse or publication being wrong. The
/// shape is therefore the same six-step high-risk pattern the profit distribution and the cohort
/// calendar repair use: read, preview a server-computed plan, confirm that plan by its hash with a
/// reason, and leave a full-content revision behind. Nothing is applied from a request that did not
/// carry a plan hash matching a plan the server just recomputed.
/// </remarks>
public static class SourceCatalogEndpoints
{
    private static readonly JsonSerializerOptions AuditMetadataOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSourceCatalogEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder catalog = builder
            .MapGroup("/api/admin/source-catalog")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Source Administration");

        catalog.MapGet("/", ReadAsync)
            .WithSummary("Returns the catalog document on disk, with its hash and validity.");

        catalog.MapPost("/preview", PreviewAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Validates a proposed catalog and returns the change plan it would apply.");

        catalog.MapPost("/apply", ApplyAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Writes the confirmed catalog, syncs the source rows and records a revision.");

        catalog.MapGet("/revisions", ListRevisionsAsync)
            .WithSummary("Lists the stored catalog revisions, newest first.");

        catalog.MapGet("/revisions/{revisionId:guid}", FindRevisionAsync)
            .WithSummary("Returns one stored catalog revision with its full document.");

        return builder;
    }

    private static async Task<IResult> ReadAsync(
        ScheduleSourceCatalogEditingService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ReadAsync(cancellationToken));

    private static async Task<IResult> PreviewAsync(
        PreviewSourceCatalogRequest request,
        ScheduleSourceCatalogEditingService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return Results.Ok(await service.PreviewAsync(
                request.Content,
                request.BaseContentHash,
                cancellationToken));
        }
        catch (ScheduleSourceCatalogValidationException exception)
        {
            return InvalidCatalog(exception.Message);
        }
        catch (ScheduleSourceCatalogConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> ApplyAsync(
        ApplySourceCatalogRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        ScheduleSourceCatalogEditingService service,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid actorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        string actorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal);

        ScheduleSourceCatalogApplyResult result;
        try
        {
            result = await service.ApplyAsync(
                new ScheduleSourceCatalogApplyCommand
                {
                    Content = request.Content,
                    BaseContentHash = request.BaseContentHash,
                    PlanHash = request.PlanHash,
                    Reason = request.Reason,
                    ActorUserId = actorUserId,
                    ActorEmail = actorEmail,
                    CorrelationId = context.CorrelationId(),
                },
                cancellationToken);
        }
        catch (ScheduleSourceCatalogValidationException exception)
        {
            return InvalidCatalog(exception.Message);
        }
        catch (ScheduleSourceCatalogConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (IOException exception)
        {
            // The catalog lives outside both hosts' release directories, so a write failure is
            // almost always a deployment that did not grant the path (systemd ReadWritePaths).
            // Saying so is more useful than a bare 500.
            return Results.Problem(
                title: "Katalog dosyası yazılamadı",
                detail: $"Katalog dosyasına yazılamadı: {exception.Message}",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // The full before/after documents already committed with the revision row; this entry is
        // what puts the change in the one activity log an operator actually reads. A crash between
        // the two loses the index entry, never the history (the ADR-093 pattern).
        await audit.RecordAsync(
            new AuditEventDraft
            {
                Category = AuditEventCategory.ScheduleSourceCatalogUpdated,
                ActorUserId = actorUserId,
                ActorEmail = actorEmail,
                SubjectType = "ScheduleSourceCatalog",
                SubjectId = result.RevisionId.ToString(),
                CorrelationId = context.CorrelationId(),
                ClientIp = context.ClientIp(),
                UserAgent = context.ClientUserAgent(),
                Reason = request.Reason,
                Metadata = JsonSerializer.Serialize(
                    new
                    {
                        revisionId = result.RevisionId,
                        contentHash = result.ContentHash,
                        previousContentHash = result.Plan.BaseContentHash,
                        sourceCount = result.Plan.SourceCount,
                        added = result.Plan.Added.Select(change => change.SourceId),
                        removed = result.Plan.Removed.Select(change => change.SourceId),
                        modified = result.Plan.Modified.Select(change => change.SourceId),
                        pollingDisabled = result.PollingDisabledSourceIds,
                        highRisk = result.Plan.HasHighRiskChange,
                    },
                    AuditMetadataOptions),
            },
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> ListRevisionsAsync(
        ScheduleSourceCatalogEditingService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListRevisionsAsync(cancellationToken));

    private static async Task<IResult> FindRevisionAsync(
        Guid revisionId,
        ScheduleSourceCatalogEditingService service,
        CancellationToken cancellationToken) =>
        await service.FindRevisionAsync(revisionId, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : Results.Problem(
                title: "Revision not found",
                detail: $"No catalog revision with ID '{revisionId}' exists.",
                statusCode: StatusCodes.Status404NotFound);

    private static IResult InvalidCatalog(string detail) => Results.Problem(
        title: "Geçersiz kaynak kataloğu",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult Conflict(string detail) => Results.Problem(
        title: "Katalog değişmiş",
        detail: detail,
        statusCode: StatusCodes.Status409Conflict);
}
