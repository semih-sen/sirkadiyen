using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Application.Auditing;

/// <summary>
/// The append-only store for account-access and activity events, and the read side that the
/// access-log and unified-audit admin surfaces query (AI_GUIDELINE §15, §19).
/// </summary>
public interface IAuditEventStore
{
    /// <summary>Persists one audit event. Records are never updated or deleted.</summary>
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>Returns a filtered, paged page of events, newest first, with the IP masked.</summary>
    Task<PagedResult<AuditEventView>> QueryAsync(
        AuditEventQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one event with the IP still masked, or <see langword="null"/> if absent.</summary>
    Task<AuditEventView?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the encrypted full IP ciphertext for one event, or <see langword="null"/> when the
    /// event does not exist or recorded no IP. Used only by the audited unmask action.
    /// </summary>
    Task<string?> GetProtectedIpAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Filters for an audit-event query. All filters are optional and combine with AND.</summary>
public sealed record AuditEventQuery
{
    public AuditEventCategory? Category { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? ActorEmail { get; init; }

    public string? SubjectType { get; init; }

    public string? SubjectId { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

/// <summary>
/// A safe read projection of one audit event: the IP is masked and the ciphertext is never
/// exposed. <see cref="HasProtectedIp"/> tells the UI an unmask is possible.
/// </summary>
public sealed record AuditEventView
{
    public required Guid Id { get; init; }

    public required AuditEventCategory Category { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? ActorEmail { get; init; }

    public string? SubjectType { get; init; }

    public string? SubjectId { get; init; }

    public string? CorrelationId { get; init; }

    public string? MaskedIp { get; init; }

    public required bool HasProtectedIp { get; init; }

    public string? UserAgent { get; init; }

    public string? Reason { get; init; }

    public string? Metadata { get; init; }
}
