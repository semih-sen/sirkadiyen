using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// The SuperAdmin surface for reading and editing the student roster catalog document (ADR-134).
/// </summary>
/// <remarks>
/// The roster catalog says which published student list belongs to which cohort and what each of
/// its columns states, so an edit here decides what a student's profile is filled in with at
/// onboarding. A wrong value map does not fail: it puts a cohort into another group's practicals.
/// The shape is therefore the same as the source catalog's (ADR-114): read, preview a
/// server-computed plan, confirm that plan by its hash with a reason, and leave a full-content
/// revision behind. Nothing is applied from a request that did not carry a plan hash matching a
/// plan the server just recomputed.
/// </remarks>
public static class RosterCatalogEndpoints
{
    private static readonly JsonSerializerOptions AuditMetadataOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapRosterCatalogEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder catalog = builder
            .MapGroup("/api/admin/roster-catalog")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Source Administration");

        catalog.MapGet("/", ReadAsync)
            .WithSummary("Returns the roster catalog document on disk, with its hash and validity.");

        catalog.MapPost("/preview", PreviewAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Validates a proposed roster catalog and returns the change plan.");

        catalog.MapPost("/apply", ApplyAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Writes the confirmed roster catalog and records a revision.");

        catalog.MapGet("/revisions", ListRevisionsAsync)
            .WithSummary("Lists the stored roster catalog revisions, newest first.");

        catalog.MapGet("/revisions/{revisionId:guid}", FindRevisionAsync)
            .WithSummary("Returns one stored roster catalog revision with its full document.");

        return builder;
    }

    private static async Task<IResult> ReadAsync(
        StudentRosterCatalogEditingService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ReadAsync(cancellationToken));

    private static async Task<IResult> PreviewAsync(
        PreviewRosterCatalogRequest request,
        StudentRosterCatalogEditingService service,
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
        catch (StudentRosterCatalogValidationException exception)
        {
            return InvalidCatalog(exception.Message);
        }
        catch (StudentRosterCatalogConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> ApplyAsync(
        ApplyRosterCatalogRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        StudentRosterCatalogEditingService service,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid actorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        string actorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal);

        StudentRosterCatalogApplyResult result;
        try
        {
            result = await service.ApplyAsync(
                new StudentRosterCatalogApplyCommand
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
        catch (StudentRosterCatalogValidationException exception)
        {
            return InvalidCatalog(exception.Message);
        }
        catch (StudentRosterCatalogConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (IOException exception)
        {
            // The catalog lives outside the host's release directory, so a write failure is almost
            // always a deployment that did not grant the path (systemd ReadWritePaths). Saying so
            // is more useful than a bare 500.
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
                Category = AuditEventCategory.StudentRosterCatalogUpdated,
                ActorUserId = actorUserId,
                ActorEmail = actorEmail,
                SubjectType = "StudentRosterCatalog",
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
                        rosterCount = result.Plan.RosterCount,
                        added = result.Plan.Added.Select(change => change.RosterId),
                        removed = result.Plan.Removed.Select(change => change.RosterId),
                        modified = result.Plan.Modified.Select(change => change.RosterId),
                        highRisk = result.Plan.HasHighRiskChange,
                    },
                    AuditMetadataOptions),
            },
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> ListRevisionsAsync(
        StudentRosterCatalogEditingService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListRevisionsAsync(cancellationToken));

    private static async Task<IResult> FindRevisionAsync(
        Guid revisionId,
        StudentRosterCatalogEditingService service,
        CancellationToken cancellationToken) =>
        await service.FindRevisionAsync(revisionId, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : Results.Problem(
                title: "Revision not found",
                detail: $"No roster catalog revision with ID '{revisionId}' exists.",
                statusCode: StatusCodes.Status404NotFound);

    private static IResult InvalidCatalog(string detail) => Results.Problem(
        title: "Geçersiz öğrenci listesi kataloğu",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult Conflict(string detail) => Results.Problem(
        title: "Katalog değişmiş",
        detail: detail,
        statusCode: StatusCodes.Status409Conflict);
}
