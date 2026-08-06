using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.Onboarding;
using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Api.Licensing;

public sealed record RedeemLicenseRequest
{
    public required string? Code { get; init; }
}

public sealed record RedeemLicenseResponse
{
    public required LicenseRedemptionOutcome Outcome { get; init; }

    public Guid? LicenseId { get; init; }

    public required OnboardingSnapshot Onboarding { get; init; }
}

/// <summary>
/// The current user's activation state.
/// </summary>
/// <remarks>
/// No expiry or "time remaining" is returned: Sirkadiyen access does not lapse after activation
/// (a license is single-use activation, not a subscription window). <c>ActivatedAtUtc</c> lets the
/// UI show when access began; a suspended account also carries <c>RevokedAtUtc</c>.
/// </remarks>
public sealed record LicenseStatusResponse
{
    public required UserLicenseState State { get; init; }

    public LicenseKind? Kind { get; init; }

    public DateTimeOffset? ActivatedAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }
}

public sealed record CreateLicenseRequest
{
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record RevokeLicenseRequest
{
    public required string Reason { get; init; }
}

public sealed record ManualActivationRequest
{
    public required string Reason { get; init; }
}
