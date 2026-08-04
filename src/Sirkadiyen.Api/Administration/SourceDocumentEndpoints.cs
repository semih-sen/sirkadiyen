using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Application.ScheduleSources;
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

/// <summary>
/// A source an administrator may upload a document for.
/// </summary>
/// <remarks>
/// It carries the shared-document group so the caller can say which other sources
/// one upload will serve (ADR-080), and the expected document format so it can
/// refuse a file the endpoint would refuse anyway. It deliberately carries no poll
/// timestamps: an upload source is never polled, so those are always absent and
/// the upload history is the audit endpoint's answer.
/// </remarks>
public sealed record UploadableSourceView
{
    public required string SourceId { get; init; }

    public required string DisplayName { get; init; }

    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required ScheduleDocumentFormat DocumentFormat { get; init; }

    /// <summary>
    /// The group of sources served by literally the same file, or
    /// <see langword="null"/> when this source has its own document.
    /// </summary>
    public string? SharedDocumentGroup { get; init; }

    /// <summary>
    /// The administratively acquired sources of a catalog, ordered by identifier so
    /// the list a UI renders does not depend on catalog order.
    /// </summary>
    public static IReadOnlyList<UploadableSourceView> SelectUploadable(
        IReadOnlyList<ScheduleSource> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return
        [
            .. catalog
                .Where(source => source.Transport is ScheduleSourceTransport.AdministrativeUpload)
                .OrderBy(source => source.SourceId.Value, StringComparer.Ordinal)
                .Select(From),
        ];
    }

    public static UploadableSourceView From(ScheduleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new UploadableSourceView
        {
            SourceId = source.SourceId.Value,
            DisplayName = source.DisplayName,
            AcademicYear = source.AcademicYear,
            ClassYear = source.ClassYear,
            ProgramLanguage = source.ProgramLanguage,
            DocumentFormat = source.DocumentFormat,
            SharedDocumentGroup = source.SharedDocumentGroup,
        };
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

    public Guid? SnapshotId { get; init; }
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
