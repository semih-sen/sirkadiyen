using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class UserStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RepeatedGoogleSignInReusesUserAndRefreshesProfile()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserStore store = new(context);
        string nonce = Guid.NewGuid().ToString("N");

        UserSession first = await store.SignInWithGoogleAsync(
            Identity($"subject-{nonce}", $"first-{nonce}@example.com", "First"),
            UserRole.User,
            Now,
            Token);
        UserSession second = await store.SignInWithGoogleAsync(
            Identity($"subject-{nonce}", $"second-{nonce}@example.com", "Second"),
            UserRole.User,
            Now.AddHours(1),
            Token);

        Assert.Equal(first.UserId, second.UserId);
        Assert.Equal($"second-{nonce}@example.com", second.Email);
        Assert.Equal("Second", second.DisplayName);
        Assert.Equal(Now.AddHours(1), second.LastSignedInAtUtc);
    }

    [Fact]
    public async Task OneVerifiedEmailCannotLinkToTwoGoogleSubjects()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        UserStore store = new(context);
        string nonce = Guid.NewGuid().ToString("N");
        string email = $"same-{nonce}@example.com";

        await store.SignInWithGoogleAsync(
            Identity($"subject-a-{nonce}", email, null),
            UserRole.User,
            Now,
            Token);

        await Assert.ThrowsAsync<GoogleIdentityConflictException>(() =>
            store.SignInWithGoogleAsync(
                Identity($"subject-b-{nonce}", email.ToUpperInvariant(), null),
                UserRole.User,
                Now,
                Token));
    }

    [Fact]
    public async Task ConcurrentFirstSignInCreatesOnlyOneLocalUser()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext firstContext = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext secondContext = fixture.CreateProductionLikeContext();
        UserStore firstStore = new(firstContext);
        UserStore secondStore = new(secondContext);
        string nonce = Guid.NewGuid().ToString("N");
        GoogleIdentity identity = Identity(
            $"concurrent-subject-{nonce}",
            $"concurrent-{nonce}@example.com",
            "Concurrent");

        UserSession[] sessions = await Task.WhenAll(
            firstStore.SignInWithGoogleAsync(
                identity,
                UserRole.User,
                Now,
                Token),
            secondStore.SignInWithGoogleAsync(
                identity,
                UserRole.User,
                Now,
                Token));

        Assert.Equal(sessions[0].UserId, sessions[1].UserId);
    }

    private static GoogleIdentity Identity(
        string subject,
        string email,
        string? displayName) => new()
        {
            Subject = subject,
            Email = email,
            EmailVerified = true,
            DisplayName = displayName,
        };

    private static readonly CancellationToken Token =
        TestContext.Current.CancellationToken;
}
