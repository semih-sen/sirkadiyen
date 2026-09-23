namespace Sirkadiyen.Api.Identity;

public static class RateLimitingPolicies
{
    public const string GoogleSignIn = nameof(GoogleSignIn);

    public const string LicenseRedemption = nameof(LicenseRedemption);

    public const string CalendarReconcile = nameof(CalendarReconcile);

    /// <summary>
    /// The student-list lookup, which answers a ten-digit guess with a name.
    /// </summary>
    public const string RosterLookup = nameof(RosterLookup);

    /// <summary>Vault note submission, which spends the owner's Claude subscription (ADR-168).</summary>
    public const string VaultNote = nameof(VaultNote);
}
