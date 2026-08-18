using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Application.Auditing;

/// <summary>The caller-facing description of an event to record.</summary>
public sealed record AuditEventDraft
{
    public required AuditEventCategory Category { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? ActorEmail { get; init; }

    public string? SubjectType { get; init; }

    public string? SubjectId { get; init; }

    public string? CorrelationId { get; init; }

    /// <summary>The raw client IP; masked for storage and encrypted for later unmasking.</summary>
    public string? ClientIp { get; init; }

    public string? UserAgent { get; init; }

    public string? Reason { get; init; }

    /// <summary>
    /// Structured detail about the event, as a JSON document. The column is <c>jsonb</c>, so
    /// anything else is rejected by the database at insert time rather than by the compiler:
    /// serialize an object, never hand-format a delimited string.
    /// </summary>
    public string? Metadata { get; init; }
}
