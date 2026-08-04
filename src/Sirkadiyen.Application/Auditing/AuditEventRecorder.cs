using System.Net;
using System.Net.Sockets;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Application.Auditing;

/// <summary>
/// Builds and persists an <see cref="AuditEvent"/> from a caller-supplied draft, applying the
/// clock, IP masking, and at-rest IP encryption so no call site has to remember those rules.
/// </summary>
public sealed class AuditEventRecorder(
    IAuditEventStore store,
    IAuditIpProtector ipProtector,
    TimeProvider timeProvider)
{
    public Task RecordAsync(AuditEventDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        string? maskedIp = AuditIp.Mask(draft.ClientIp);

        // Only a parseable address is kept. When it parses, the full value is encrypted at rest so
        // an unmask can later reveal it; the masked form is what every ordinary read returns.
        string? protectedIp = maskedIp is null
            ? null
            : ipProtector.Protect(draft.ClientIp!);

        AuditEvent auditEvent = AuditEvent.Create(
            draft.Category,
            timeProvider.GetUtcNow(),
            draft.ActorUserId,
            draft.ActorEmail,
            draft.SubjectType,
            draft.SubjectId,
            draft.CorrelationId,
            maskedIp,
            protectedIp,
            draft.UserAgent,
            draft.Reason,
            draft.Metadata);

        return store.AppendAsync(auditEvent, cancellationToken);
    }
}

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

    public string? Metadata { get; init; }
}

/// <summary>Encrypts and decrypts the full client IP kept behind a masked audit record.</summary>
public interface IAuditIpProtector
{
    string Protect(string plaintextIp);

    string Unprotect(string ciphertext);
}

/// <summary>Reduces a client IP to a coarse, privacy-preserving form for default display.</summary>
public static class AuditIp
{
    /// <summary>
    /// Returns the address with its host bits cleared — the last octet for IPv4, the low 80 bits
    /// (keeping the /48 prefix) for IPv6 — or <see langword="null"/> when the value is missing or
    /// not a valid address.
    /// </summary>
    public static string? Mask(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip.Trim(), out IPAddress? address))
        {
            return null;
        }

        byte[] bytes = address.GetAddressBytes();
        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                bytes[3] = 0;
                return new IPAddress(bytes).ToString();

            case AddressFamily.InterNetworkV6:
                for (int index = 6; index < bytes.Length; index++)
                {
                    bytes[index] = 0;
                }

                return new IPAddress(bytes).ToString();

            default:
                return null;
        }
    }
}
