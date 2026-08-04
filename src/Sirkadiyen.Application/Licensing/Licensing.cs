using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Application.Licensing;

public interface ILicenseCodeService
{
    GeneratedLicenseCode Generate();

    bool TryHash(string plaintextCode, out byte[] codeHash);
}

public sealed record GeneratedLicenseCode
{
    public required string PlaintextCode { get; init; }

    public required byte[] CodeHash { get; init; }
}

public interface ILicenseStore
{
    Task SaveCreatedAsync(License license, CancellationToken cancellationToken);

    Task<LicenseRedemptionResult> RedeemAsync(
        byte[] codeHash,
        Guid userId,
        string userEmail,
        DateTimeOffset redeemedAtUtc,
        CancellationToken cancellationToken);

    Task<LicenseRevocationResult> RevokeAsync(
        Guid licenseId,
        Guid actorUserId,
        string actorEmail,
        string reason,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken);

    Task<ManualLicenseActivationResult> ActivateManuallyAsync(
        Guid userId,
        Guid actorUserId,
        string actorEmail,
        string reason,
        DateTimeOffset activatedAtUtc,
        CancellationToken cancellationToken);

    Task<UserLicenseState> GetUserLicenseStateAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The license that currently governs one user's access, or <see langword="null"/> when the
    /// user has never activated. Prefers the redeemed (active) license, falling back to the most
    /// recently revoked one so a suspended user can be shown why. Never exposes the code hash.
    /// </summary>
    Task<UserLicenseSummary?> GetUserLicenseSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken);
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

public sealed class LicenseHashCollisionException(string message) : Exception(message);

/// <summary>Coordinates license creation, redemption, and revocation.</summary>
public sealed class LicenseService(
    ILicenseCodeService codeService,
    ILicenseStore store,
    TimeProvider timeProvider)
{
    private const int MaximumGenerationAttempts = 3;

    public async Task<CreatedLicense> CreateAsync(
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset? expiresAtUtc,
        string? notes,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaximumGenerationAttempts; attempt++)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            GeneratedLicenseCode generated = codeService.Generate();
            License license = License.Create(
                generated.CodeHash,
                actorUserId,
                actorEmail,
                now,
                expiresAtUtc,
                notes);

            try
            {
                await store.SaveCreatedAsync(license, cancellationToken);
                return new CreatedLicense
                {
                    LicenseId = license.Id,
                    PlaintextCode = generated.PlaintextCode,
                    Status = license.Status,
                    ExpiresAtUtc = license.ExpiresAtUtc,
                    CreatedAtUtc = license.CreatedAtUtc,
                };
            }
            catch (LicenseHashCollisionException) when (attempt < MaximumGenerationAttempts)
            {
                // Generate a new high-entropy code. The plaintext from the
                // colliding attempt has not escaped this method.
            }
        }

        throw new LicenseHashCollisionException(
            "A unique license code could not be generated after multiple attempts.");
    }

    public Task<LicenseRedemptionResult> RedeemAsync(
        string plaintextCode,
        Guid userId,
        string userEmail,
        CancellationToken cancellationToken)
    {
        if (!codeService.TryHash(plaintextCode, out byte[] codeHash))
        {
            return Task.FromResult(new LicenseRedemptionResult
            {
                Outcome = LicenseRedemptionOutcome.Invalid,
            });
        }

        return store.RedeemAsync(
            codeHash,
            userId,
            userEmail,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<LicenseRevocationResult> RevokeAsync(
        Guid licenseId,
        Guid actorUserId,
        string actorEmail,
        string reason,
        CancellationToken cancellationToken) => store.RevokeAsync(
            licenseId,
            actorUserId,
            actorEmail,
            reason,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<ManualLicenseActivationResult> ActivateManuallyAsync(
        Guid userId,
        Guid actorUserId,
        string actorEmail,
        string reason,
        CancellationToken cancellationToken) => store.ActivateManuallyAsync(
            userId,
            actorUserId,
            actorEmail,
            reason,
            timeProvider.GetUtcNow(),
            cancellationToken);
}
