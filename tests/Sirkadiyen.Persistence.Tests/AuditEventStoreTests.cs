using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Auditing.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AuditEventStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendsAndQueriesByCategoryAndActorNewestFirst()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("audit-query");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AuditEventStore store = new(context);
        await store.AppendAsync(SignIn(user, Now, "cipher-1"), Token);
        await store.AppendAsync(SignIn(user, Now.AddMinutes(5), "cipher-2"), Token);
        await store.AppendAsync(
            AuditEvent.Create(
                AuditEventCategory.ReconcileRequested,
                Now.AddMinutes(1),
                user.UserId,
                user.Email,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            Token);

        PagedResult<AuditEventView> page = await store.QueryAsync(
            new AuditEventQuery
            {
                Category = AuditEventCategory.SignIn,
                ActorUserId = user.UserId,
            },
            Token);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(AuditEventCategory.SignIn, item.Category));
        Assert.True(page.Items[0].OccurredAtUtc >= page.Items[1].OccurredAtUtc);

        // The read projection reports that a full IP exists but never returns the ciphertext.
        Assert.All(page.Items, item => Assert.True(item.HasProtectedIp));
    }

    [Fact]
    public async Task TheCiphertextIsReturnedOnlyByTheExplicitAccessor()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("audit-unmask");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AuditEventStore store = new(context);
        AuditEvent signIn = SignIn(user, Now, "cipher-secret");
        await store.AppendAsync(signIn, Token);

        Assert.Equal("cipher-secret", await store.GetProtectedIpAsync(signIn.Id, Token));
        Assert.Null(await store.GetProtectedIpAsync(Guid.CreateVersion7(), Token));

        AuditEventView? view = await store.FindAsync(signIn.Id, Token);
        Assert.NotNull(view);
        Assert.Equal("203.0.113.0", view!.MaskedIp);
        Assert.True(view.HasProtectedIp);
    }

    [Fact]
    public async Task QueryFiltersByTimeWindow()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("audit-window");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AuditEventStore store = new(context);
        await store.AppendAsync(SignIn(user, Now, "cipher-early"), Token);
        await store.AppendAsync(SignIn(user, Now.AddHours(2), "cipher-late"), Token);

        PagedResult<AuditEventView> page = await store.QueryAsync(
            new AuditEventQuery
            {
                ActorUserId = user.UserId,
                FromUtc = Now.AddHours(1),
            },
            Token);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(Now.AddHours(2), page.Items[0].OccurredAtUtc);
    }

    [Fact]
    public async Task MetadataMustBeAJsonDocumentBecauseTheColumnIsJsonb()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("audit-metadata");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AuditEventStore store = new(context);

        AuditEvent recorded = WithMetadata(user, Now, """{"planHash":"abc","users":1}""");
        await store.AppendAsync(recorded, Token);

        AuditEventView? view = await store.FindAsync(recorded.Id, Token);
        Assert.NotNull(view);
        Assert.Contains("planHash", view!.Metadata);

        // A hand-formatted delimited string compiles, since the property is a plain string, and
        // is then rejected by the database at insert time. Callers must serialize an object.
        await using SirkadiyenDbContext second = fixture.CreateProductionLikeContext();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new AuditEventStore(second).AppendAsync(
                WithMetadata(user, Now.AddMinutes(1), "planHash=abc;users=1"),
                Token));
    }

    private static AuditEvent WithMetadata(UserSession user, DateTimeOffset at, string metadata) =>
        AuditEvent.Create(
            AuditEventCategory.CalendarRepairRequested,
            at,
            user.UserId,
            user.Email,
            "CalendarRepair",
            "2026-2027/3/Turkish",
            "correlation-id",
            null,
            null,
            null,
            "cleaning up",
            metadata);

    private static AuditEvent SignIn(UserSession user, DateTimeOffset at, string protectedIp) =>
        AuditEvent.Create(
            AuditEventCategory.SignIn,
            at,
            user.UserId,
            user.Email,
            null,
            null,
            "correlation-id",
            "203.0.113.0",
            protectedIp,
            "test-agent",
            null,
            null);

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
