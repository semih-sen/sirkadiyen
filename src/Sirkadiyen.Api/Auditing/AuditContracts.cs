namespace Sirkadiyen.Api.Auditing;

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
