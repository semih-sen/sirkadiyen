using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class StudentProfileStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProfileIsInsertedAndReadBackWithSelectors()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("profile-insert");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        StudentProfileStore store = new(context);

        StudentProfileView saved = await store.UpsertAsync(
            user.UserId,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "1.0",
            new Dictionary<string, string>
            {
                ["practiceGroup"] = "A",
                ["practiceSubgroup"] = "A1",
            },
            Now,
            Token);

        Assert.Equal(user.UserId, saved.UserId);
        Assert.True(await store.ExistsForUserAsync(user.UserId, Token));

        StudentProfileView? read = await store.GetByUserIdAsync(user.UserId, Token);
        Assert.NotNull(read);
        Assert.Equal("2025-2026", read.AcademicYear);
        Assert.Equal(1, read.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, read.ProgramLanguage);
        Assert.Equal("A", read.Selectors["practiceGroup"]);
        Assert.Equal("A1", read.Selectors["practiceSubgroup"]);
    }

    [Fact]
    public async Task ReSavingReplacesTheSameRowRatherThanAddingASecond()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("profile-upsert");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        StudentProfileStore store = new(context);

        await store.UpsertAsync(
            user.UserId,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "1.0",
            new Dictionary<string, string> { ["practiceGroup"] = "A", ["practiceSubgroup"] = "A1" },
            Now,
            Token);
        StudentProfileView updated = await store.UpsertAsync(
            user.UserId,
            "2025-2026",
            1,
            ProgramLanguage.English,
            "1.0",
            new Dictionary<string, string> { ["practiceGroup"] = "İ", ["practiceSubgroup"] = "İ2" },
            Now.AddMinutes(5),
            Token);

        Assert.Equal(ProgramLanguage.English, updated.ProgramLanguage);
        Assert.Equal("İ2", updated.Selectors["practiceSubgroup"]);
        Assert.Equal(Now.AddMinutes(5), updated.UpdatedAtUtc);

        await using SirkadiyenDbContext verification = fixture.CreateContext();
        Assert.Equal(
            1,
            await verification.StudentProfiles.CountAsync(
                profile => profile.UserId == user.UserId,
                Token));
    }

    [Fact]
    public async Task ConcurrentFirstTimeSavesConvergeOnOneRow()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("profile-race");

        await using SirkadiyenDbContext firstContext = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext secondContext = fixture.CreateProductionLikeContext();

        await Task.WhenAll(
            new StudentProfileStore(firstContext).UpsertAsync(
                user.UserId,
                "2025-2026",
                1,
                ProgramLanguage.Turkish,
                "1.0",
                new Dictionary<string, string> { ["practiceGroup"] = "A", ["practiceSubgroup"] = "A1" },
                Now,
                Token),
            new StudentProfileStore(secondContext).UpsertAsync(
                user.UserId,
                "2025-2026",
                1,
                ProgramLanguage.Turkish,
                "1.0",
                new Dictionary<string, string> { ["practiceGroup"] = "B", ["practiceSubgroup"] = "B2" },
                Now.AddSeconds(1),
                Token));

        await using SirkadiyenDbContext verification = fixture.CreateContext();
        Assert.Equal(
            1,
            await verification.StudentProfiles.CountAsync(
                profile => profile.UserId == user.UserId,
                Token));
    }

    [Fact]
    public async Task NoProfileReadsAsAbsent()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("profile-absent");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        StudentProfileStore store = new(context);

        Assert.False(await store.ExistsForUserAsync(user.UserId, Token));
        Assert.Null(await store.GetByUserIdAsync(user.UserId, Token));
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
            UserRole.User,
            Now,
            Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
