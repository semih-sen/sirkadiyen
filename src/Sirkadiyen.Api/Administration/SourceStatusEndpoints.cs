using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// Read-only SuperAdmin views over the ingestion pipeline's per-source health, backing the source
/// status dashboard, plus the one mutating maintenance action it exposes: pruning an old snapshot's
/// stored payload (ADR-120). The reads never poll or parse; the prune removes only the recoverable
/// payload and is audited.
/// </summary>
public static class SourceStatusEndpoints
{
    public static IEndpointRouteBuilder MapSourceStatusEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder sources = builder
            .MapGroup("/api/admin/sources")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Source Administration");

        sources.MapGet("/", ListAsync)
            .WithSummary("Lists every source with poll status and its latest parse run and revision.");

        sources.MapGet("/{sourceId}", FindAsync)
            .WithSummary("Returns one source's status with its parser profile and recent snapshots.");

        sources.MapPost("/snapshots/{snapshotId:guid}/prune-payload", PrunePayloadAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Removes one snapshot's stored payload, keeping its immutable metadata.")
            .WithDescription(
                "The operator-triggered counterpart to automatic retention (ADR-044): it reclaims "
                + "the large normalized document while the snapshot's identity, hashes, counts and "
                + "every downstream parse/revision/diff decision remain. Refused for the newest "
                + "snapshot, the current year's baseline, a snapshot still needed for parser "
                + "recovery, and while the source's pipeline is frozen. Requires a reason and is "
                + "audited.");

        return builder;
    }

    private static async Task<IResult> ListAsync(
        ISourceStatusReadStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(cancellationToken));

    private static async Task<IResult> FindAsync(
        string sourceId,
        ISourceStatusReadStore store,
        CancellationToken cancellationToken) =>
        await store.FindAsync(sourceId, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : Results.Problem(
                title: "Source not found",
                detail: $"No source with ID '{sourceId}' exists.",
                statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> PrunePayloadAsync(
        Guid snapshotId,
        PruneSnapshotPayloadRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        SnapshotPayloadPruneService service,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > AuditEvent.MaximumReasonLength)
        {
            return Results.Problem(
                title: "A reason is required",
                detail: "'reason' is required and must be at most "
                    + $"{AuditEvent.MaximumReasonLength} characters. Removing a snapshot's payload "
                    + "is an audited action.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        SnapshotPayloadPruneResult result = await service.PruneAsync(snapshotId, cancellationToken);

        switch (result.Outcome)
        {
            case SnapshotPayloadPruneOutcome.Pruned:
                // Dropping evidence, even the recoverable payload, is recorded with who, why and
                // which source/acquisition it was (AI_GUIDELINE §9, §19).
                await audit.RecordAsync(
                    new AuditEventDraft
                    {
                        Category = AuditEventCategory.SnapshotPayloadPruned,
                        ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                        ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                        SubjectType = "SourceSnapshot",
                        SubjectId = snapshotId.ToString(),
                        CorrelationId = context.CorrelationId(),
                        ClientIp = context.ClientIp(),
                        UserAgent = context.ClientUserAgent(),
                        Reason = reason,
                        Metadata = JsonSerializer.Serialize(new
                        {
                            sourceId = result.SourceId,
                            acquiredAtUtc = result.AcquiredAtUtc,
                        }),
                    },
                    cancellationToken);

                return Results.Ok(new PruneSnapshotPayloadResponse
                {
                    SnapshotId = snapshotId,
                    SourceId = result.SourceId!,
                    AcquiredAtUtc = result.AcquiredAtUtc!.Value,
                });

            case SnapshotPayloadPruneOutcome.SnapshotNotFound:
                return Results.Problem(
                    title: "Snapshot not found",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status404NotFound);

            // A frozen pipeline is a temporary operational state, not a bad request: the same prune
            // succeeds once the freeze is lifted.
            case SnapshotPayloadPruneOutcome.Frozen:
                return Results.Problem(
                    title: "Pipeline frozen",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status409Conflict);

            case SnapshotPayloadPruneOutcome.AlreadyPruned:
                return Results.Problem(
                    title: "Payload already pruned",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status409Conflict);

            default:
                return Results.Problem(
                    title: "Snapshot payload cannot be pruned",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status409Conflict);
        }
    }
}

/// <summary>The reason an operator gives for removing a snapshot's payload (ADR-120).</summary>
public sealed record PruneSnapshotPayloadRequest
{
    public string? Reason { get; init; }
}

public sealed record PruneSnapshotPayloadResponse
{
    public required Guid SnapshotId { get; init; }

    public required string SourceId { get; init; }

    public required DateTimeOffset AcquiredAtUtc { get; init; }
}
