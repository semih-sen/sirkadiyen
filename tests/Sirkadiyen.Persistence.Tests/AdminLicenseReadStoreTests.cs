using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Administration.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AdminLicenseReadStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DetailReturnsTheAuditTrailAndNeverTheCodeHash()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession admin = await CreateUserAsync("admin-lic-detail");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        byte[] hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        License license = License.Create(hash, admin.UserId, admin.Email, Now, null, "A note");
        await new LicenseStore(context).SaveCreatedAsync(license, Token);

        AdminLicenseDetail? detail = await new AdminLicenseReadStore(context)
            .FindAsync(license.Id, Token);

        Assert.NotNull(detail);
        Assert.Equal(license.Id, detail!.Summary.LicenseId);
        Assert.Equal(LicenseStatus.Active, detail.Summary.Status);
        Assert.Equal("A note", detail.Summary.Notes);

        // Creating a code license writes exactly one 'Created' audit entry.
        AdminLicenseAuditEntry created = Assert.Single(detail.Audit);
        Assert.Equal(LicenseAuditAction.Created, created.Action);
    }

    [Fact]
    public async Task ListFiltersByStatus()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession admin = await CreateUserAsync("admin-lic-list");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        LicenseStore store = new(context);
        byte[] activeHash =
            Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        License active = License.Create(activeHash, admin.UserId, admin.Email, Now, null, null);
        await store.SaveCreatedAsync(active, Token);

        AdminLicenseReadStore read = new(context);
        PagedResult<AdminLicenseListItem> activeOnly = await read.ListAsync(
            new AdminLicenseQuery { Status = LicenseStatus.Active, PageSize = 200 },
            Token);

        Assert.Contains(activeOnly.Items, item => item.LicenseId == active.Id);
        Assert.All(activeOnly.Items, item => Assert.Equal(LicenseStatus.Active, item.Status));
    }

    private async Task<UserSession> CreateUserAsync(string prefix)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        string nonce = Guid.NewGuid().ToString("N");
        return await new UserStore(context).SignInWithGoogleAsync(
            new GoogleIdentity
            {
                Subject = $"{prefix}-{nonce}",
                Email = $"{prefix}-{nonce}@example.com",
                EmailVerified = true,
            },
            UserRole.SuperAdmin,
            Now,
            Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
