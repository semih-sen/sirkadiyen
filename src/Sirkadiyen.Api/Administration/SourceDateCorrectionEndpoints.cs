using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// The operator's answer to a date the parser refused to correct on its own
/// (ADR-139).
/// </summary>
/// <remarks>
/// The parser reads every date column chronologically and repairs a mistyped year
/// where the dates around it leave exactly one reading. Where they do not — the
/// cell contradicts its own weekday, or two years fit equally well — it publishes
/// the date as written and reports the readings that fit. The revision is held,
/// and until now nothing on any screen could resolve it: the document is the
/// faculty's, and re-parsing it produced the same wrong date every time.
/// <para>
/// A correction accepted here is source configuration, not an edit to a parsed
/// record, so the next parse of the same snapshot applies it and produces the same
/// records (ADR-017). It is also part of the parse run's key, so the correction
/// takes effect on the next ordinary poll rather than needing a forced re-parse.
/// </para>
/// </remarks>
public static class SourceDateCorrectionEndpoints
{
    public static IEndpointRouteBuilder MapSourceDateCorrectionEndpoints(
        this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Every source's corrections in one read, for the screen that answers "which dates are we
        // overriding, and are they still needed?" — a correction outlives the revision it was
        // decided from and keeps applying on every later parse.
        builder
            .MapGet("/api/admin/sources/date-corrections", ListAllAsync)
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Source Administration")
            .WithSummary("Lists every stored source date correction, newest decision first.");

        RouteGroupBuilder corrections = builder
            .MapGroup("/api/admin/sources/{sourceId}/date-corrections")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Source Administration");

        corrections.MapGet("/", ListAsync)
            .WithSummary("Lists the dates an operator has decided this source states wrongly.");

        corrections.MapPost("/", AcceptAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Accepts that this source writes one date where it means another.")
            .WithDescription(
                "The correction applies wherever the source writes the original date, on this "
                + "and every future parse (ADR-139). It replaces any correction the source "
                + "already has for the same original date, so an operator who picks the other "
                + "candidate after reading the suggestion again simply accepts again. Requires a "
                + "reason and is audited: this is the one way a lesson reaches a calendar on a "
                + "day no document states.");

        corrections.MapDelete("/{correctionId:guid}", RetireAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Retires a correction, normally because the document was fixed.");

        return builder;
    }

    private static async Task<IResult> ListAllAsync(
        IScheduleSourceDateCorrectionStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(
            (await store.ListAllAsync(cancellationToken)).Select(Describe).ToList());

    private static async Task<IResult> ListAsync(
        string sourceId,
        ISourceStatusReadStore status,
        IScheduleSourceDateCorrectionStore store,
        CancellationToken cancellationToken)
    {
        if (await status.FindAsync(sourceId, cancellationToken) is null)
        {
            return NotFound(sourceId);
        }

        IReadOnlyList<ScheduleSourceDateCorrection> corrections =
            await store.ListForSourceAsync(SourceId.Parse(sourceId), cancellationToken);

        return Results.Ok(corrections.Select(Describe).ToList());
    }

    private static async Task<IResult> AcceptAsync(
        string sourceId,
        AcceptSourceDateCorrectionRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        ISourceStatusReadStore status,
        IScheduleSourceDateCorrectionStore store,
        AuditEventRecorder audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Confirmed against the running catalog rather than trusted from the
        // route, so a mistyped id is a 404 rather than a correction stored
        // against a source that does not exist and silently never applied.
        if (await status.FindAsync(sourceId, cancellationToken) is null)
        {
            return NotFound(sourceId);
        }

        string reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > AuditEvent.MaximumReasonLength)
        {
            return Results.Problem(
                title: "A reason is required",
                detail: "'reason' is required and must be at most "
                    + $"{AuditEvent.MaximumReasonLength} characters. Correcting a date the "
                    + "document states is an audited decision.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The original and corrected dates are allowed to be equal: that is the
        // operator confirming the source states this date correctly despite it
        // sitting out of sequence, which stops every later parse from holding the
        // revision over it (ADR-139).
        ScheduleSourceDateCorrection correction = new(
            SourceId.Parse(sourceId),
            request.Original,
            request.Corrected,
            UserClaimsPrincipalFactory.GetRequiredEmail(principal),
            timeProvider.GetUtcNow(),
            reason);

        await store.AcceptAsync(correction, cancellationToken);

        await audit.RecordAsync(
            new AuditEventDraft
            {
                Category = AuditEventCategory.SourceDateCorrectionAccepted,
                ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                SubjectType = "ScheduleSource",
                SubjectId = sourceId,
                CorrelationId = context.CorrelationId(),
                ClientIp = context.ClientIp(),
                UserAgent = context.ClientUserAgent(),
                Reason = reason,
                Metadata = JsonSerializer.Serialize(new
                {
                    original = Invariant(request.Original),
                    corrected = Invariant(request.Corrected),
                }),
            },
            cancellationToken);

        return Results.Ok(Describe(correction));
    }

    private static async Task<IResult> RetireAsync(
        string sourceId,
        Guid correctionId,
        ClaimsPrincipal principal,
        HttpContext context,
        IScheduleSourceDateCorrectionStore store,
        AuditEventRecorder audit,
        CancellationToken cancellationToken)
    {
        if (!await store.RetireAsync(SourceId.Parse(sourceId), correctionId, cancellationToken))
        {
            return Results.Problem(
                title: "Correction not found",
                detail: $"Source '{sourceId}' has no date correction '{correctionId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await audit.RecordAsync(
            new AuditEventDraft
            {
                Category = AuditEventCategory.SourceDateCorrectionRetired,
                ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                SubjectType = "ScheduleSource",
                SubjectId = sourceId,
                CorrelationId = context.CorrelationId(),
                ClientIp = context.ClientIp(),
                UserAgent = context.ClientUserAgent(),
                Metadata = JsonSerializer.Serialize(new { correctionId }),
            },
            cancellationToken);

        return Results.NoContent();
    }

    private static IResult NotFound(string sourceId) =>
        Results.Problem(
            title: "Source not found",
            detail: $"No source with ID '{sourceId}' exists.",
            statusCode: StatusCodes.Status404NotFound);

    private static SourceDateCorrectionResponse Describe(ScheduleSourceDateCorrection correction) =>
        new()
        {
            Id = correction.Id,
            SourceId = correction.SourceId.Value,
            Original = correction.Original,
            Corrected = correction.Corrected,
            DecidedBy = correction.DecidedBy,
            DecidedAtUtc = correction.DecidedAtUtc,
            Note = correction.Note,
        };

    private static string Invariant(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>An operator accepting that a source writes one date and means another (ADR-139).</summary>
public sealed record AcceptSourceDateCorrectionRequest
{
    /// <summary>The date the document resolves to today.</summary>
    public required DateOnly Original { get; init; }

    /// <summary>The date it means — normally one of the candidates the parser listed.</summary>
    public required DateOnly Corrected { get; init; }

    /// <summary>Why, in the operator's own words. Required: this decision is audited.</summary>
    public string? Reason { get; init; }
}

public sealed record SourceDateCorrectionResponse
{
    public required Guid Id { get; init; }

    public required string SourceId { get; init; }

    public required DateOnly Original { get; init; }

    public required DateOnly Corrected { get; init; }

    public required string DecidedBy { get; init; }

    public required DateTimeOffset DecidedAtUtc { get; init; }

    public required string Note { get; init; }
}
