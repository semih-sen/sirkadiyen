using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class LicenseStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RedemptionIsIdempotentAndAudited()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (UserSession admin, UserSession user) = await CreateUsersAsync("idempotent");
        byte[] hash = RandomHash();
        Guid licenseId;

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            License license = License.Create(
                hash,
                admin.UserId,
                admin.Email,
                Now,
                null,
                "Persistence test");
            licenseId = license.Id;
            await new LicenseStore(context).SaveCreatedAsync(license, Token);
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            LicenseStore store = new(context);
            LicenseRedemptionResult first = await store.RedeemAsync(
                hash,
                user.UserId,
                user.Email,
                Now.AddMinutes(1),
                Token);
            LicenseRedemptionResult repeated = await store.RedeemAsync(
                hash,
                user.UserId,
                user.Email,
                Now.AddMinutes(2),
                Token);

            Assert.Equal(LicenseRedemptionOutcome.Redeemed, first.Outcome);
            Assert.Equal(
                LicenseRedemptionOutcome.AlreadyRedeemedByCurrentUser,
                repeated.Outcome);
            Assert.Equal(UserLicenseState.Active, await store.GetUserLicenseStateAsync(
                user.UserId,
                Token));
            Assert.Equal(
                2,
                await context.LicenseAudits.CountAsync(
                    audit => audit.LicenseId == licenseId,
                    Token));
        }
    }

    [Fact]
    public async Task SameCodeCanBeWonByOnlyOneConcurrentUser()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession admin = await CreateUserAsync("admin-same-code", UserRole.SuperAdmin);
        UserSession firstUser = await CreateUserAsync("first-same-code", UserRole.User);
        UserSession secondUser = await CreateUserAsync("second-same-code", UserRole.User);
        byte[] hash = RandomHash();
        await CreateLicenseAsync(hash, admin);

        await using SirkadiyenDbContext firstContext = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext secondContext = fixture.CreateProductionLikeContext();
        LicenseRedemptionResult[] results = await Task.WhenAll(
            new LicenseStore(firstContext).RedeemAsync(
                hash,
                firstUser.UserId,
                firstUser.Email,
                Now.AddMinutes(1),
                Token),
            new LicenseStore(secondContext).RedeemAsync(
                hash,
                secondUser.UserId,
                secondUser.Email,
                Now.AddMinutes(1),
                Token));

        Assert.Single(
            results,
            result => result.Outcome == LicenseRedemptionOutcome.Redeemed);
        Assert.Single(
            results,
            result => result.Outcome == LicenseRedemptionOutcome.RedeemedByAnotherUser);
    }

    [Fact]
    public async Task UserCanWinOnlyOneOfTwoConcurrentCodes()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession admin = await CreateUserAsync("admin-two-codes", UserRole.SuperAdmin);
        UserSession user = await CreateUserAsync("student-two-codes", UserRole.User);
        byte[] firstHash = RandomHash();
        byte[] secondHash = RandomHash();
        await CreateLicenseAsync(firstHash, admin);
        await CreateLicenseAsync(secondHash, admin);

        await using SirkadiyenDbContext firstContext = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext secondContext = fixture.CreateProductionLikeContext();
        LicenseRedemptionResult[] results = await Task.WhenAll(
            new LicenseStore(firstContext).RedeemAsync(
                firstHash,
                user.UserId,
                user.Email,
                Now.AddMinutes(1),
                Token),
            new LicenseStore(secondContext).RedeemAsync(
                secondHash,
                user.UserId,
                user.Email,
                Now.AddMinutes(1),
                Token));

        Assert.Single(
            results,
            result => result.Outcome == LicenseRedemptionOutcome.Redeemed);
        Assert.Single(
            results,
            result => result.Outcome == LicenseRedemptionOutcome.UserAlreadyActivated);
    }

    [Fact]
    public async Task RevokingRedeemedLicenseSuspendsUserWithoutDeletingHistory()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (UserSession admin, UserSession user) = await CreateUsersAsync("revoke");
        byte[] hash = RandomHash();
        Guid licenseId = await CreateLicenseAsync(hash, admin);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        LicenseStore store = new(context);
        await store.RedeemAsync(
            hash,
            user.UserId,
            user.Email,
            Now.AddMinutes(1),
            Token);
        LicenseRevocationResult revoked = await store.RevokeAsync(
            licenseId,
            admin.UserId,
            admin.Email,
            "Administrative revocation.",
            Now.AddMinutes(2),
            Token);

        Assert.Equal(LicenseRevocationOutcome.Revoked, revoked.Outcome);
        Assert.Equal(user.UserId, revoked.AffectedUserId);
        Assert.Equal(
            UserLicenseState.Suspended,
            await store.GetUserLicenseStateAsync(user.UserId, Token));
        Assert.Equal(
            3,
            await context.LicenseAudits.CountAsync(
                audit => audit.LicenseId == licenseId,
                Token));
    }

    [Fact]
    public async Task ManualActivationIsIdempotentAndHasNoCodeHash()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (UserSession admin, UserSession user) = await CreateUsersAsync("manual");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        LicenseStore store = new(context);
        ManualLicenseActivationResult first = await store.ActivateManuallyAsync(
            user.UserId,
            admin.UserId,
            admin.Email,
            "Activated by the administrator.",
            Now,
            Token);
        ManualLicenseActivationResult repeated = await store.ActivateManuallyAsync(
            user.UserId,
            admin.UserId,
            admin.Email,
            "Repeated panel request.",
            Now.AddMinutes(1),
            Token);

        Assert.Equal(ManualLicenseActivationOutcome.Activated, first.Outcome);
        Assert.Equal(ManualLicenseActivationOutcome.AlreadyActivated, repeated.Outcome);
        License license = await context.Licenses
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == first.LicenseId, Token);
        Assert.Equal(LicenseKind.Manual, license.Kind);
        Assert.Null(license.CodeHash);
        Assert.Equal(
            LicenseAuditAction.ManuallyActivated,
            await context.LicenseAudits
                .Where(audit => audit.LicenseId == license.Id)
                .Select(audit => audit.Action)
                .SingleAsync(Token));
    }

    [Fact]
    public async Task ManualActivationAndCodeRedemptionHaveOnlyOneConcurrentWinner()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession admin = await CreateUserAsync("admin-manual-race", UserRole.SuperAdmin);
        UserSession user = await CreateUserAsync("student-manual-race", UserRole.User);
        byte[] hash = RandomHash();
        await CreateLicenseAsync(hash, admin);

        await using SirkadiyenDbContext manualContext = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext codeContext = fixture.CreateProductionLikeContext();
        Task<ManualLicenseActivationResult> manualTask =
            new LicenseStore(manualContext).ActivateManuallyAsync(
                user.UserId,
                admin.UserId,
                admin.Email,
                "Concurrent manual activation.",
                Now.AddMinutes(1),
                Token);
        Task<LicenseRedemptionResult> codeTask = new LicenseStore(codeContext).RedeemAsync(
            hash,
            user.UserId,
            user.Email,
            Now.AddMinutes(1),
            Token);

        await Task.WhenAll(manualTask, codeTask);
        ManualLicenseActivationResult manualResult = await manualTask;
        LicenseRedemptionResult codeResult = await codeTask;
        bool manualWon = manualResult.Outcome
            == ManualLicenseActivationOutcome.Activated;
        bool codeWon = codeResult.Outcome == LicenseRedemptionOutcome.Redeemed;

        Assert.NotEqual(manualWon, codeWon);
        Assert.Equal(
            manualWon
                ? ManualLicenseActivationOutcome.Activated
                : ManualLicenseActivationOutcome.AlreadyActivated,
            manualResult.Outcome);
        Assert.Equal(
            codeWon
                ? LicenseRedemptionOutcome.Redeemed
                : LicenseRedemptionOutcome.UserAlreadyActivated,
            codeResult.Outcome);

        await using SirkadiyenDbContext verification = fixture.CreateContext();
        Assert.Equal(
            1,
            await verification.Licenses.CountAsync(
                license => license.RedeemedByUserId == user.UserId
                    && license.Status == LicenseStatus.Redeemed,
                Token));
    }

    [Fact]
    public async Task SummaryIsNullWhenUserNeverActivated()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("summary-none", UserRole.User);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserLicenseSummary? summary =
            await new LicenseStore(context).GetUserLicenseSummaryAsync(user.UserId, Token);

        Assert.Null(summary);
    }

    [Fact]
    public async Task SummaryReportsTheRedeemedLicense()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (UserSession admin, UserSession user) = await CreateUsersAsync("summary-active");
        byte[] hash = RandomHash();
        await CreateLicenseAsync(hash, admin);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        LicenseStore store = new(context);
        await store.RedeemAsync(hash, user.UserId, user.Email, Now.AddMinutes(1), Token);

        UserLicenseSummary? summary = await store.GetUserLicenseSummaryAsync(user.UserId, Token);

        Assert.NotNull(summary);
        Assert.Equal(LicenseStatus.Redeemed, summary!.Status);
        Assert.Equal(LicenseKind.Code, summary.Kind);
        Assert.Equal(Now.AddMinutes(1), summary.RedeemedAtUtc);
        Assert.Null(summary.RevokedAtUtc);
    }

    [Fact]
    public async Task SummaryReportsRevocationWhenSuspended()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (UserSession admin, UserSession user) = await CreateUsersAsync("summary-suspended");
        byte[] hash = RandomHash();
        Guid licenseId = await CreateLicenseAsync(hash, admin);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        LicenseStore store = new(context);
        await store.RedeemAsync(hash, user.UserId, user.Email, Now.AddMinutes(1), Token);
        await store.RevokeAsync(
            licenseId,
            admin.UserId,
            admin.Email,
            "Administrative revocation.",
            Now.AddMinutes(2),
            Token);

        UserLicenseSummary? summary = await store.GetUserLicenseSummaryAsync(user.UserId, Token);

        Assert.NotNull(summary);
        Assert.Equal(LicenseStatus.Revoked, summary!.Status);
        Assert.Equal(Now.AddMinutes(2), summary.RevokedAtUtc);
    }

    private async Task<(UserSession Admin, UserSession User)> CreateUsersAsync(string prefix) => (
        await CreateUserAsync($"admin-{prefix}", UserRole.SuperAdmin),
        await CreateUserAsync($"user-{prefix}", UserRole.User));

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

    private async Task<Guid> CreateLicenseAsync(byte[] hash, UserSession admin)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        License license = License.Create(
            hash,
            admin.UserId,
            admin.Email,
            Now,
            null,
            null);
        await new LicenseStore(context).SaveCreatedAsync(license, Token);
        return license.Id;
    }

    private static byte[] RandomHash() => Guid.NewGuid().ToByteArray()
        .Concat(Guid.NewGuid().ToByteArray())
        .ToArray();

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
