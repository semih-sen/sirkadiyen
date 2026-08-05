using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
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

        StudentProfileView saved = (await store.UpsertAsync(
            user.UserId,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "0101240048",
            "1.0",
            new Dictionary<string, string>
            {
                ["practiceGroup"] = "A",
                ["practiceSubgroup"] = "A1",
            },
            Now,
            Token)).Profile;

        Assert.Equal(user.UserId, saved.UserId);
        Assert.Equal("0101240048", saved.StudentNumber);
        Assert.True(await store.ExistsForUserAsync(user.UserId, Token));

        StudentProfileView? read = await store.GetByUserIdAsync(user.UserId, Token);
        Assert.NotNull(read);
        Assert.Equal("2025-2026", read.AcademicYear);
        Assert.Equal(1, read.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, read.ProgramLanguage);
        Assert.Equal("0101240048", read.StudentNumber);
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
            "0101240048",
            "1.0",
            new Dictionary<string, string> { ["practiceGroup"] = "A", ["practiceSubgroup"] = "A1" },
            Now,
            Token);
        StudentProfileView updated = (await store.UpsertAsync(
            user.UserId,
            "2025-2026",
            1,
            ProgramLanguage.English,
            "0102240048",
            "1.0",
            new Dictionary<string, string> { ["practiceGroup"] = "İ", ["practiceSubgroup"] = "İ2" },
            Now.AddMinutes(5),
            Token)).Profile;

        Assert.Equal(ProgramLanguage.English, updated.ProgramLanguage);
        Assert.Equal("0102240048", updated.StudentNumber);
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
                "0101240048",
                "1.0",
                new Dictionary<string, string> { ["practiceGroup"] = "A", ["practiceSubgroup"] = "A1" },
                Now,
                Token),
            new StudentProfileStore(secondContext).UpsertAsync(
                user.UserId,
                "2025-2026",
                1,
                ProgramLanguage.Turkish,
                "0101240048",
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

    [Fact]
    public async Task AnAudienceChangeQueuesACalendarResyncInTheSameTransaction()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("profile-resync");
        await CompleteConnectionAsync(user.UserId, "resync@group.calendar.google.com");

        StudentProfileUpsertResult first = await SaveAsync(user.UserId, "A", "0101240048", Now);
        StudentProfileUpsertResult moved = await SaveAsync(
            user.UserId,
            "B",
            "0101240048",
            Now.AddMinutes(5));

        // A first profile is never an audience change: initial sync resolves it when it runs.
        Assert.False(first.AudienceChanged);
        Assert.False(first.CalendarResyncRequested);

        Assert.True(moved.AudienceChanged);
        Assert.True(moved.CalendarResyncRequested);

        await using SirkadiyenDbContext verification = fixture.CreateContext();
        GoogleCalendarConnection connection = await verification.GoogleCalendarConnections
            .AsNoTracking()
            .SingleAsync(candidate => candidate.UserId == user.UserId, Token);
        Assert.Equal(Now.AddMinutes(5), connection.ProfileResyncRequiredSinceUtc);
    }

    [Fact]
    public async Task AChangeConfinedToTheStudentNumberQueuesNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("profile-number-only");
        await CompleteConnectionAsync(user.UserId, "number@group.calendar.google.com");

        await SaveAsync(user.UserId, "A", "0101240048", Now);
        StudentProfileUpsertResult corrected = await SaveAsync(
            user.UserId,
            "A",
            "0101240049",
            Now.AddMinutes(5));

        Assert.False(corrected.AudienceChanged);
        Assert.False(corrected.CalendarResyncRequested);

        await using SirkadiyenDbContext verification = fixture.CreateContext();
        GoogleCalendarConnection connection = await verification.GoogleCalendarConnections
            .AsNoTracking()
            .SingleAsync(candidate => candidate.UserId == user.UserId, Token);
        Assert.Null(connection.ProfileResyncRequiredSinceUtc);
    }

    [Fact]
    public async Task AnAudienceChangeBeforeInitialSyncCompletesQueuesNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession user = await CreateUserAsync("profile-resync-early");

        await SaveAsync(user.UserId, "A", "0101240048", Now);
        StudentProfileUpsertResult moved = await SaveAsync(
            user.UserId,
            "B",
            "0101240048",
            Now.AddMinutes(5));

        // The audience did change, but there is no populated calendar to converge: initial sync
        // reads the profile as it stands when it runs (ADR-096).
        Assert.True(moved.AudienceChanged);
        Assert.False(moved.CalendarResyncRequested);
    }

    private async Task<StudentProfileUpsertResult> SaveAsync(
        Guid userId,
        string practiceGroup,
        string studentNumber,
        DateTimeOffset atUtc)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        return await new StudentProfileStore(context).UpsertAsync(
            userId,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            studentNumber,
            "1.0",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["practiceGroup"] = practiceGroup,
            },
            atUtc,
            Token);
    }

    private async Task CompleteConnectionAsync(Guid userId, string calendarId)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore store = new(context);
        await store.UpsertAuthorizationAsync(
            userId,
            "protected",
            "https://www.googleapis.com/auth/calendar.app.created",
            Now,
            Token);
        await store.RequestInitialSyncAsync(userId, Now.AddMinutes(1), Token);
        await store.AttachManagedCalendarAsync(userId, calendarId, Now.AddMinutes(2), Token);
        await store.MarkInitialSyncCompletedAsync(userId, Now.AddMinutes(3), Token);
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
