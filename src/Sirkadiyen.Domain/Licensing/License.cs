namespace Sirkadiyen.Domain.Licensing;

/// <summary>A single-use account activation license.</summary>
public sealed class License
{
    public const int CodeHashLength = 32;

    public const int MaximumActorEmailLength = 320;

    public const int MaximumNotesLength = 1000;

    public const int MaximumReasonLength = 2000;

    private License()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// A deterministic keyed hash used for lookup. The plaintext code is never
    /// persisted.
    /// </summary>
    public byte[]? CodeHash { get; private init; }

    public LicenseKind Kind { get; private init; }

    public LicenseStatus Status { get; private set; }

    /// <summary>The last instant at which an unused code may be redeemed.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; private init; }

    public string? Notes { get; private init; }

    public Guid CreatedByUserId { get; private init; }

    public string CreatedByEmail { get; private init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid? RedeemedByUserId { get; private set; }

    public DateTimeOffset? RedeemedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public string? RevokedByEmail { get; private set; }

    public string? RevocationReason { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public static License Create(
        byte[] codeHash,
        Guid createdByUserId,
        string createdByEmail,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc,
        string? notes)
    {
        ArgumentNullException.ThrowIfNull(codeHash);
        if (codeHash.Length != CodeHashLength)
        {
            throw new ArgumentException(
                $"A license code hash must contain exactly {CodeHashLength} bytes.",
                nameof(codeHash));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("A creating user is required.", nameof(createdByUserId));
        }

        createdByEmail = RequiredBounded(
            createdByEmail,
            MaximumActorEmailLength,
            nameof(createdByEmail));
        notes = OptionalBounded(notes, MaximumNotesLength, nameof(notes));

        if (expiresAtUtc is not null && expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtUtc),
                "A license expiration must be later than its creation time.");
        }

        return new License
        {
            Id = Guid.CreateVersion7(),
            CodeHash = [.. codeHash],
            Kind = LicenseKind.Code,
            Status = LicenseStatus.Active,
            ExpiresAtUtc = expiresAtUtc,
            Notes = notes,
            CreatedByUserId = createdByUserId,
            CreatedByEmail = createdByEmail,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    public static License CreateManualActivation(
        Guid userId,
        Guid createdByUserId,
        string createdByEmail,
        string reason,
        DateTimeOffset activatedAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("An activated user is required.", nameof(userId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("A creating user is required.", nameof(createdByUserId));
        }

        createdByEmail = RequiredBounded(
            createdByEmail,
            MaximumActorEmailLength,
            nameof(createdByEmail));
        _ = RequiredBounded(reason, MaximumReasonLength, nameof(reason));

        return new License
        {
            Id = Guid.CreateVersion7(),
            Kind = LicenseKind.Manual,
            Status = LicenseStatus.Redeemed,
            CreatedByUserId = createdByUserId,
            CreatedByEmail = createdByEmail,
            CreatedAtUtc = activatedAtUtc,
            UpdatedAtUtc = activatedAtUtc,
            RedeemedByUserId = userId,
            RedeemedAtUtc = activatedAtUtc,
        };
    }

    public bool IsExpiredAt(DateTimeOffset atUtc) =>
        ExpiresAtUtc is not null && atUtc >= ExpiresAtUtc;

    public LicenseAudit Redeem(
        Guid userId,
        string actorEmail,
        DateTimeOffset redeemedAtUtc)
    {
        if (Status != LicenseStatus.Active)
        {
            throw new InvalidOperationException(
                $"Only an active license can be redeemed; current status is {Status}.");
        }

        if (IsExpiredAt(redeemedAtUtc))
        {
            throw new InvalidOperationException("An expired license cannot be redeemed.");
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A redeeming user is required.", nameof(userId));
        }

        actorEmail = RequiredBounded(
            actorEmail,
            MaximumActorEmailLength,
            nameof(actorEmail));

        Status = LicenseStatus.Redeemed;
        RedeemedByUserId = userId;
        RedeemedAtUtc = redeemedAtUtc;
        UpdatedAtUtc = redeemedAtUtc;

        return LicenseAudit.Create(
            Id,
            LicenseAuditAction.Redeemed,
            userId,
            actorEmail,
            "License redeemed.",
            redeemedAtUtc);
    }

    public LicenseAudit MarkExpired(
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset expiredAtUtc)
    {
        if (Status != LicenseStatus.Active || !IsExpiredAt(expiredAtUtc))
        {
            throw new InvalidOperationException(
                "Only an active license past its expiration can be marked expired.");
        }

        Status = LicenseStatus.Expired;
        UpdatedAtUtc = expiredAtUtc;

        return LicenseAudit.Create(
            Id,
            LicenseAuditAction.Expired,
            actorUserId,
            actorEmail,
            "Redemption attempted after the license expiration.",
            expiredAtUtc);
    }

    public LicenseAudit Revoke(
        Guid actorUserId,
        string actorEmail,
        string reason,
        DateTimeOffset revokedAtUtc)
    {
        if (Status == LicenseStatus.Revoked)
        {
            throw new InvalidOperationException("The license is already revoked.");
        }

        actorEmail = RequiredBounded(
            actorEmail,
            MaximumActorEmailLength,
            nameof(actorEmail));
        reason = RequiredBounded(reason, MaximumReasonLength, nameof(reason));

        Status = LicenseStatus.Revoked;
        RevokedByUserId = actorUserId;
        RevokedByEmail = actorEmail;
        RevocationReason = reason;
        RevokedAtUtc = revokedAtUtc;
        UpdatedAtUtc = revokedAtUtc;

        return LicenseAudit.Create(
            Id,
            LicenseAuditAction.Revoked,
            actorUserId,
            actorEmail,
            reason,
            revokedAtUtc);
    }

    public LicenseAudit CreationAudit()
    {
        if (Kind != LicenseKind.Code)
        {
            throw new InvalidOperationException(
                "Only a code license has a code-creation audit.");
        }

        return LicenseAudit.Create(
            Id,
            LicenseAuditAction.Created,
            CreatedByUserId,
            CreatedByEmail,
            "License created.",
            CreatedAtUtc);
    }

    public LicenseAudit ManualActivationAudit(string reason)
    {
        if (Kind != LicenseKind.Manual)
        {
            throw new InvalidOperationException(
                "Only a manual license has a manual-activation audit.");
        }

        return LicenseAudit.Create(
            Id,
            LicenseAuditAction.ManuallyActivated,
            CreatedByUserId,
            CreatedByEmail,
            RequiredBounded(reason, MaximumReasonLength, nameof(reason)),
            CreatedAtUtc);
    }

    private static string RequiredBounded(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }

    private static string? OptionalBounded(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }
}

public enum LicenseStatus
{
    Active,
    Redeemed,
    Revoked,
    Expired,
}

public enum LicenseKind
{
    Code,
    Manual,
}

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

public enum LicenseAuditAction
{
    Created,
    Redeemed,
    ManuallyActivated,
    Revoked,
    Expired,
}
