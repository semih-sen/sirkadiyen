using Sirkadiyen.Domain.Licensing;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class LicenseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateKeepsOnlyHashAndProducesCreationAudit()
    {
        byte[] hash = Enumerable.Range(0, License.CodeHashLength)
            .Select(Convert.ToByte)
            .ToArray();
        Guid adminId = Guid.NewGuid();

        License license = License.Create(
            hash,
            adminId,
            "admin@example.com",
            Now,
            Now.AddDays(7),
            " Cohort A ");
        hash[0] = 255;
        LicenseAudit audit = license.CreationAudit();

        Assert.Equal(LicenseKind.Code, license.Kind);
        Assert.Equal(LicenseStatus.Active, license.Status);
        Assert.Equal(0, license.CodeHash![0]);
        Assert.Equal("Cohort A", license.Notes);
        Assert.Equal(LicenseAuditAction.Created, audit.Action);
        Assert.Equal(adminId, audit.ActorUserId);
        Assert.DoesNotContain("SIRK", Convert.ToHexString(license.CodeHash));
    }

    [Fact]
    public void RedeemIsSingleTransitionAndRetainsRedeemerForLaterRevocation()
    {
        Guid adminId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        License license = Create(adminId);

        LicenseAudit redemption = license.Redeem(
            userId,
            "student@example.com",
            Now.AddMinutes(1));
        LicenseAudit revocation = license.Revoke(
            adminId,
            "admin@example.com",
            "Chargeback confirmed.",
            Now.AddMinutes(2));

        Assert.Equal(LicenseStatus.Revoked, license.Status);
        Assert.Equal(userId, license.RedeemedByUserId);
        Assert.Equal(LicenseAuditAction.Redeemed, redemption.Action);
        Assert.Equal(LicenseAuditAction.Revoked, revocation.Action);
        Assert.Throws<InvalidOperationException>(() => license.Redeem(
            userId,
            "student@example.com",
            Now.AddMinutes(3)));
    }

    [Fact]
    public void ExpiredCodeCannotBeRedeemed()
    {
        License license = License.Create(
            new byte[License.CodeHashLength],
            Guid.NewGuid(),
            "admin@example.com",
            Now,
            Now.AddMinutes(1),
            null);

        LicenseAudit audit = license.MarkExpired(
            Guid.NewGuid(),
            "student@example.com",
            Now.AddMinutes(1));

        Assert.Equal(LicenseStatus.Expired, license.Status);
        Assert.Equal(LicenseAuditAction.Expired, audit.Action);
    }

    [Fact]
    public void ExpirationMustBeAfterCreation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => License.Create(
            new byte[License.CodeHashLength],
            Guid.NewGuid(),
            "admin@example.com",
            Now,
            Now,
            null));
    }

    [Fact]
    public void ManualActivationHasNoCodeAndRecordsAdminReason()
    {
        Guid userId = Guid.NewGuid();
        Guid adminId = Guid.NewGuid();

        License license = License.CreateManualActivation(
            userId,
            adminId,
            "admin@example.com",
            "WhatsApp delivery was not suitable.",
            Now);
        LicenseAudit audit = license.ManualActivationAudit(
            "WhatsApp delivery was not suitable.");

        Assert.Equal(LicenseKind.Manual, license.Kind);
        Assert.Equal(LicenseStatus.Redeemed, license.Status);
        Assert.Null(license.CodeHash);
        Assert.Equal(userId, license.RedeemedByUserId);
        Assert.Equal(LicenseAuditAction.ManuallyActivated, audit.Action);
        Assert.Equal(adminId, audit.ActorUserId);
        Assert.Equal("WhatsApp delivery was not suitable.", audit.Reason);
    }

    private static License Create(Guid adminId) => License.Create(
        new byte[License.CodeHashLength],
        adminId,
        "admin@example.com",
        Now,
        null,
        null);
}
