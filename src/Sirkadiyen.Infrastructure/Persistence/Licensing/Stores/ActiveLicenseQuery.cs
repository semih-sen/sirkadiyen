using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;

/// <summary>
/// The one definition of "this user's access is currently active", for the queries that choose
/// users to perform Calendar work for (ADR-095).
/// </summary>
/// <remarks>
/// License revocation stops future synchronization (ADR-022, systemPatterns §13). That is enforced
/// where a user is <em>selected</em> rather than inside each service, so a future selection path
/// cannot quietly omit it, and so a revocation takes effect on the next worker cycle without any
/// job having to sweep anything.
/// <para>
/// It mirrors <c>LicenseStore.GetUserLicenseStateAsync</c>: a redeemed license is what grants
/// access. A manual <c>SuperAdmin</c> activation is already a <c>Redeemed</c> license with no code
/// hash (ADR-053), so it needs no special case here.
/// </para>
/// </remarks>
internal static class ActiveLicenseQuery
{
    /// <summary>The users whose access is currently active, as a subquery.</summary>
    public static IQueryable<Guid> UserIds(SirkadiyenDbContext dbContext) =>
        dbContext.Licenses
            .Where(license =>
                license.Status == LicenseStatus.Redeemed
                && license.RedeemedByUserId != null)
            .Select(license => license.RedeemedByUserId!.Value);
}
