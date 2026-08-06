using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Application.Licensing;

public sealed record GeneratedLicenseCode
{
    public required string PlaintextCode { get; init; }

    public required byte[] CodeHash { get; init; }
}

/// <summary>
/// A safe, read-only summary of the license governing one user's access.
/// </summary>
/// <remarks>
/// Sirkadiyen licenses do not expire after redemption — <see cref="License.ExpiresAtUtc"/> is a
/// redemption deadline for an unused code, not a post-activation validity window. There is
/// therefore no "time remaining" to report; introducing time-limited access would be a separate,
/// recorded decision (AI_GUIDELINE §4, §7).
/// </remarks>
public sealed record UserLicenseSummary
{
    public required LicenseStatus Status { get; init; }

    public required LicenseKind Kind { get; init; }

    public DateTimeOffset? RedeemedAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }
}

public sealed record LicenseRedemptionResult
{
    public required LicenseRedemptionOutcome Outcome { get; init; }

    public Guid? LicenseId { get; init; }
}

public enum LicenseRedemptionOutcome
{
    Redeemed,
    AlreadyRedeemedByCurrentUser,
    UserAlreadyActivated,
    Invalid,
    Expired,
    Revoked,
    RedeemedByAnotherUser,
}

public sealed record LicenseRevocationResult
{
    public required LicenseRevocationOutcome Outcome { get; init; }

    public Guid? AffectedUserId { get; init; }
}

public enum LicenseRevocationOutcome
{
    Revoked,
    AlreadyRevoked,
    NotFound,
}

public sealed record ManualLicenseActivationResult
{
    public required ManualLicenseActivationOutcome Outcome { get; init; }

    public Guid? LicenseId { get; init; }

    public required Guid UserId { get; init; }
}

public enum ManualLicenseActivationOutcome
{
    Activated,
    AlreadyActivated,
    UserNotFound,
}

public enum UserLicenseState
{
    None,
    Active,
    Suspended,
}

public sealed record CreatedLicense
{
    public required Guid LicenseId { get; init; }

    /// <summary>Returned exactly once and never persisted.</summary>
    public required string PlaintextCode { get; init; }

    public required LicenseStatus Status { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
