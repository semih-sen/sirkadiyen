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
