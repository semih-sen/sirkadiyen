namespace Sirkadiyen.Domain.Licensing;

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

public enum LicenseAuditAction
{
    Created,
    Redeemed,
    ManuallyActivated,
    Revoked,
    Expired,
}
