using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Application.Licensing;

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
