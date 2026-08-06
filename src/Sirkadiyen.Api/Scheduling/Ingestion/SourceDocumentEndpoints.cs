using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.Scheduling.Ingestion;

/// <summary>
/// Administrative acquisition for sources that are handed out rather than
/// published (ADR-079, ADR-080).
/// </summary>
public static class SourceDocumentEndpoints
{
    public static IEndpointRouteBuilder MapSourceDocumentEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder sources = builder
            .MapGroup("/api/sources")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Schedule Sources");

        sources.MapGet("/uploadable", ListUploadableAsync)
            .WithSummary("Returns the sources that are acquired by administrative upload.")
            .WithDescription(
                "The catalog is server-owned and changes at academic-year rollover, so the "
                + "administration UI asks which sources accept an upload rather than "
                + "restating the list.");

        sources.MapPost("/{sourceId}/document", UploadAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Uploads the document for an administratively acquired source.")
            .WithDescription(
                "Stores the uploaded document as an immutable snapshot for every source the "
                + "document serves. It does not parse or publish: the worker does that on its "
                + "next cycle, under the same rules as a polled source.");

        sources.MapGet("/{sourceId}/document/uploads", ListUploadsAsync)
            .WithSummary("Returns the recent upload history for one source.");

        return builder;
    }

    private static async Task<IResult> ListUploadableAsync(
        IScheduleSourceStore sourceStore,
        CancellationToken cancellationToken)
    {
        // An upload source is never polling-enabled (ADR-079), so the whole
        // catalog is read and the transport decides, not the polling flag.
        IReadOnlyList<ScheduleSource> catalog = await sourceStore.ListAsync(
            onlyPollingEnabled: false,
            cancellationToken);

        return Results.Ok(UploadableSourceView.SelectUploadable(catalog));
    }

    private static async Task<IResult> UploadAsync(
        string sourceId,
        IFormFile? file,
        ClaimsPrincipal principal,
        HttpContext context,
        AdministrativeDocumentUploadService uploadService,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                title: "No document uploaded",
                detail: "Send the document as the multipart form field 'file'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Checked before reading so an oversized upload is refused rather than
        // buffered; the service enforces the same bound on what it actually read.
        if (file.Length > AdministrativeDocumentUploadService.MaximumDocumentBytes)
        {
            return Results.Problem(
                title: "Document too large",
                detail: "The document exceeds "
                    + $"{AdministrativeDocumentUploadService.MaximumDocumentBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        using MemoryStream buffer = new((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);

        DocumentUploadResult result = await uploadService.UploadAsync(
            new DocumentUploadRequest
            {
                SourceId = sourceId,

                // The submitted name is evidence, not a path: it is recorded and
                // never used to open, write or resolve anything on disk.
                FileName = Path.GetFileName(file.FileName ?? string.Empty),
                Content = buffer.ToArray(),
                UploadedBy = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                CorrelationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            },
            cancellationToken);

        return result.Outcome switch
        {
            DocumentUploadOutcome.Accepted => Results.Ok(UploadResponse.From(result)),

            DocumentUploadOutcome.SourceNotFound => Results.Problem(
                title: "Source not found",
                detail: result.Detail,
                statusCode: StatusCodes.Status404NotFound),

            DocumentUploadOutcome.DocumentTooLarge => Results.Problem(
                title: "Document too large",
                detail: result.Detail,
                statusCode: StatusCodes.Status413PayloadTooLarge),

            // A frozen pipeline is a temporary operational state, not a bad
            // request: the same upload succeeds once the freeze is lifted.
            DocumentUploadOutcome.Frozen => Results.Problem(
                title: "Pipeline frozen",
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),

            _ => Results.Problem(
                title: "Document rejected",
                detail: result.Detail,
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static async Task<IResult> ListUploadsAsync(
        string sourceId,
        ISourceDocumentUploadAuditStore auditStore,
        CancellationToken cancellationToken)
    {
        const int limit = 20;

        if (!SourceId.TryParse(sourceId, out SourceId parsed))
        {
            return Results.Problem(
                title: "Source not found",
                detail: $"'{sourceId}' is not a valid source identifier.",
                statusCode: StatusCodes.Status404NotFound);
        }

        IReadOnlyList<SourceDocumentUpload> uploads = await auditStore.ListForSourceAsync(
            parsed,
            limit,
            cancellationToken);

        return Results.Ok(uploads.Select(UploadAuditEntry.From));
    }
}
