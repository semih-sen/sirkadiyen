namespace Sirkadiyen.Domain.Licensing;

/// <summary>An append-only record of a security-sensitive license transition.</summary>
public sealed class LicenseAudit
{
    private LicenseAudit()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    public Guid LicenseId { get; private init; }

    public LicenseAuditAction Action { get; private init; }

    public Guid ActorUserId { get; private init; }

    public string ActorEmail { get; private init; } = string.Empty;

    public string Reason { get; private init; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private init; }

    internal static LicenseAudit Create(
        Guid licenseId,
        LicenseAuditAction action,
        Guid actorUserId,
        string actorEmail,
        string reason,
        DateTimeOffset occurredAtUtc)
    {
        if (licenseId == Guid.Empty)
        {
            throw new ArgumentException("A license is required.", nameof(licenseId));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(actorEmail);
        actorEmail = actorEmail.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            actorEmail.Length,
            License.MaximumActorEmailLength,
            nameof(actorEmail));

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        reason = reason.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            reason.Length,
            License.MaximumReasonLength,
            nameof(reason));

        return new LicenseAudit
        {
            Id = Guid.CreateVersion7(),
            LicenseId = licenseId,
            Action = action,
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            Reason = reason,
            OccurredAtUtc = occurredAtUtc,
        };
    }
}
