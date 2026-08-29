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
}
