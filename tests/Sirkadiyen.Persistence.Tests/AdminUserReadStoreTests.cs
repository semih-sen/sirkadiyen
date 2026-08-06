using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Domain.StudentProfiles;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Administration.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AdminUserReadStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListProjectsLicenseStateAndProfilePresenceForAMatchingEmail()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession admin = await CreateUserAsync("admin-list", UserRole.SuperAdmin);
        UserSession student = await CreateUserAsync("student-list", UserRole.User);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await ActivateAsync(context, admin, student);
        await AddProfileAsync(context, student.UserId);

        AdminUserReadStore store = new(context);
        PagedResult<AdminUserListItem> page = await store.ListAsync(
            new AdminUserQuery { Search = student.Email },
            Token);

        AdminUserListItem item = Assert.Single(page.Items);
        Assert.Equal(student.UserId, item.Id);
        Assert.Equal(UserLicenseState.Active, item.LicenseState);
        Assert.True(item.HasProfile);
        Assert.Equal(UserRole.User, item.Role);
    }

    [Fact]
    public async Task DetailComposesProfileLicensesAndEventCount()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession admin = await CreateUserAsync("admin-detail", UserRole.SuperAdmin);
        UserSession student = await CreateUserAsync("student-detail", UserRole.User);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await ActivateAsync(context, admin, student);
        await AddProfileAsync(context, student.UserId);

        AdminUserDetail? detail = await new AdminUserReadStore(context)
            .FindAsync(student.UserId, Token);

        Assert.NotNull(detail);
        Assert.Equal(UserLicenseState.Active, detail!.Summary.LicenseState);
        Assert.NotNull(detail.Profile);
        Assert.Equal(1, detail.Profile!.ClassYear);
        Assert.Single(detail.Licenses);
        Assert.Equal(LicenseStatus.Redeemed, detail.Licenses[0].Status);
        Assert.Equal(0, detail.ManagedEventCount);
    }

    [Fact]
    public async Task UnknownUserDetailIsNull()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Null(await new AdminUserReadStore(context).FindAsync(Guid.CreateVersion7(), Token));
    }

    private static async Task ActivateAsync(
        SirkadiyenDbContext context,
        UserSession admin,
        UserSession student)
    {
        byte[] hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        License license = License.Create(hash, admin.UserId, admin.Email, Now, null, null);
        LicenseStore store = new(context);
        await store.SaveCreatedAsync(license, Token);
        await store.RedeemAsync(hash, student.UserId, student.Email, Now.AddMinutes(1), Token);
    }

    private static async Task AddProfileAsync(SirkadiyenDbContext context, Guid userId)
    {
        context.StudentProfiles.Add(StudentProfile.Create(
            userId,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "0102030405",
            "1.1",
            new Dictionary<string, string> { ["practiceGroup"] = "A" },
            Now));
        await context.SaveChangesAsync(Token);
    }

    private async Task<UserSession> CreateUserAsync(string prefix, UserRole role)
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
            role,
            Now,
            Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
