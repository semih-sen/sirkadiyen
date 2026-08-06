using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Application.Licensing;

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
