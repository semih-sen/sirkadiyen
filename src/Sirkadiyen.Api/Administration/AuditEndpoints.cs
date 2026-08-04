using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Api.Administration;

/// <summary>
/// SuperAdmin views over the account-access and activity log: a sign-in-scoped access log with the
/// IP masked by default, a separately audited unmask action, and a unified cross-category query
/// (AI_GUIDELINE §15, §19, constraint K7).
/// </summary>
public static class AuditEndpoints
{
    private const int MaximumPageSize = 200;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder accessLogs = builder
            .MapGroup("/api/admin/access-logs")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Access Logs");

        accessLogs.MapGet("/", ListAccessLogsAsync)
            .WithSummary("Lists sign-in events, newest first, with the client IP masked.");

        accessLogs.MapPost("/{id:guid}/unmask", UnmaskAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Reveals the full client IP for one event; itself an audited action.");

        RouteGroupBuilder audit = builder
            .MapGroup("/api/admin/audit")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Audit");

        audit.MapGet("/", QueryAsync)
            .WithSummary("Queries the account-access and activity log across every category.");

        return builder;
    }

    private static async Task<IResult> ListAccessLogsAsync(
        IAuditEventStore store,
        CancellationToken cancellationToken,
        Guid? userId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            return InvalidPageSize();
        }

        return Results.Ok(await store.QueryAsync(
            new AuditEventQuery
            {
                Category = AuditEventCategory.SignIn,
                ActorUserId = userId,
                FromUtc = from,
                ToUtc = to,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken));
    }

    private static async Task<IResult> QueryAsync(
        IAuditEventStore store,
        CancellationToken cancellationToken,
        AuditEventCategory? category = null,
        Guid? actorUserId = null,
        string? actorEmail = null,
        string? subjectType = null,
        string? subjectId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            return InvalidPageSize();
        }

        return Results.Ok(await store.QueryAsync(
            new AuditEventQuery
            {
                Category = category,
                ActorUserId = actorUserId,
                ActorEmail = actorEmail,
                SubjectType = subjectType,
                SubjectId = subjectId,
                FromUtc = from,
                ToUtc = to,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken));
    }

    private static async Task<IResult> UnmaskAsync(
        Guid id,
        UnmaskAuditIpRequest request,
        ClaimsPrincipal principal,
        IAuditEventStore store,
        IAuditIpProtector ipProtector,
        AuditEventRecorder audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > AuditEvent.MaximumReasonLength)
        {
            return Results.Problem(
                title: "A reason is required",
                detail: $"'reason' is required and must be at most "
                    + $"{AuditEvent.MaximumReasonLength} characters. Revealing a client IP is an "
                    + "audited action.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        AuditEventView? auditEvent = await store.FindAsync(id, cancellationToken);
        if (auditEvent is null)
        {
            return Results.Problem(
                title: "Audit event not found",
                detail: $"No audit event with ID '{id}' exists.",
                statusCode: StatusCodes.Status404NotFound);
        }

        string? ciphertext = await store.GetProtectedIpAsync(id, cancellationToken);
        if (ciphertext is null)
        {
            return Results.Problem(
                title: "No IP to reveal",
                detail: "This audit event recorded no client IP.",
                statusCode: StatusCodes.Status409Conflict);
        }

        string ip = ipProtector.Unprotect(ciphertext);

        // Revealing a masked IP is itself an audited access event, recording who did it and why.
        await audit.RecordAsync(
            new AuditEventDraft
            {
                Category = AuditEventCategory.IpUnmasked,
                ActorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                ActorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                SubjectType = "AuditEvent",
                SubjectId = id.ToString(),
                CorrelationId = context.CorrelationId(),
                ClientIp = context.ClientIp(),
                UserAgent = context.ClientUserAgent(),
                Reason = reason,
            },
            cancellationToken);

        return Results.Ok(new UnmaskAuditIpResponse { AuditEventId = id, Ip = ip });
    }

    private static IResult InvalidPageSize() => Results.Problem(
        title: "Invalid page size",
        detail: $"'pageSize' must be between 1 and {MaximumPageSize}.",
        statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>Why the authenticated SuperAdmin is revealing a masked client IP.</summary>
public sealed record UnmaskAuditIpRequest
{
    /// <example>Investigating repeated failed sign-ins reported by the user.</example>
    public required string? Reason { get; init; }
}

public sealed record UnmaskAuditIpResponse
{
    public required Guid AuditEventId { get; init; }

    public required string Ip { get; init; }
}
