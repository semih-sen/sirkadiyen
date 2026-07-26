using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Api.Administration;

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

public sealed record UploadResponse
{
    /// <summary>The digest of the uploaded bytes, which identifies the file itself.</summary>
    public required string ContentSha256 { get; init; }

    /// <summary>Every source the document became evidence for, and what changed.</summary>
    public required IReadOnlyList<UploadTargetResponse> Targets { get; init; }

    public static UploadResponse From(DocumentUploadResult result) => new()
    {
        ContentSha256 = result.ContentSha256!,
        Targets = [.. result.Targets.Select(target => new UploadTargetResponse
        {
            SourceId = target.SourceId,
            ClassYear = target.ClassYear,
            ProgramLanguage = target.ProgramLanguage,
            Outcome = target.Outcome,
            SnapshotId = target.SnapshotId,
        })],
    };
}

public sealed record UploadTargetResponse
{
    public required string SourceId { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required SourceDocumentUploadOutcome Outcome { get; init; }

    public required Guid SnapshotId { get; init; }
}

public sealed record UploadAuditEntry
{
    public required string SourceId { get; init; }

    public required string UploadedBy { get; init; }

    public required string FileName { get; init; }

    public required long ByteCount { get; init; }

    public required string ContentSha256 { get; init; }

    public required SourceDocumentUploadOutcome Outcome { get; init; }

    public required DateTimeOffset UploadedAtUtc { get; init; }

    public static UploadAuditEntry From(SourceDocumentUpload upload) => new()
    {
        SourceId = upload.SourceId.Value,
        UploadedBy = upload.UploadedBy,
        FileName = upload.FileName,
        ByteCount = upload.ByteCount,
        ContentSha256 = upload.ContentSha256,
        Outcome = upload.Outcome,
        UploadedAtUtc = upload.UploadedAtUtc,
    };
}
