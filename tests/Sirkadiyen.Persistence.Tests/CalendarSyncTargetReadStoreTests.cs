using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Proves the incremental fan-out only ever reaches users who can actually receive writes —
/// authorized, initial-sync-completed, in the right cohort (ADR-059).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CalendarSyncTargetReadStoreTests(PostgresFixture fixture)
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.app.created";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private enum Stage
    {
        InProgress,
        Completed,
        CompletedThenReauth,
    }

    [Fact]
    public async Task OnlyCompletedAuthorizedUsersInTheCohortAreCohortTargets()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid ready = await SetUpAsync("cohort-ready", 1, ProgramLanguage.Turkish, Stage.Completed);
        // Not yet finished initial sync — their calendar is populated by that stage, not this one.
        Guid inProgress = await SetUpAsync("cohort-inprogress", 1, ProgramLanguage.Turkish, Stage.InProgress);
        // Dead credential — must be skipped, not written to.
        Guid reauth = await SetUpAsync("cohort-reauth", 1, ProgramLanguage.Turkish, Stage.CompletedThenReauth);
        // Completed, but a different cohort.
        Guid otherYear = await SetUpAsync("cohort-otheryear", 2, ProgramLanguage.Turkish, Stage.Completed);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IReadOnlyList<CalendarSyncTarget> targets = await new CalendarSyncTargetReadStore(context)
            .ListCohortTargetsAsync("2025-2026", 1, ProgramLanguage.Turkish, Token);

        // The fixture's database is shared across the collection, so assert about our own users only.
        CalendarSyncTarget mine = Assert.Single(targets, target => target.UserId == ready);
        Assert.Equal("cal-cohort-ready", mine.ManagedCalendarId);
        Assert.Equal("protected:tok-cohort-ready", mine.ProtectedRefreshToken);
        Assert.Equal("A", mine.Profile.Selectors["practiceGroup"]);
        Assert.DoesNotContain(targets, target => target.UserId == inProgress);
        Assert.DoesNotContain(targets, target => target.UserId == reauth);
        Assert.DoesNotContain(targets, target => target.UserId == otherYear);
    }

    [Fact]
    public async Task TargetsByUserIdReturnsOnlyTheReadyOnesAmongThoseRequested()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid ready = await SetUpAsync("byid-ready", 1, ProgramLanguage.Turkish, Stage.Completed);
        Guid inProgress = await SetUpAsync("byid-inprogress", 1, ProgramLanguage.Turkish, Stage.InProgress);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IReadOnlyList<CalendarSyncTarget> targets = await new CalendarSyncTargetReadStore(context)
            .ListTargetsByUserIdsAsync([ready, inProgress, Guid.CreateVersion7()], Token);

        CalendarSyncTarget only = Assert.Single(targets);
        Assert.Equal(ready, only.UserId);
    }

    private async Task<Guid> SetUpAsync(
        string prefix,
        int classYear,
        ProgramLanguage programLanguage,
        Stage stage)
    {
        UserSession user = await CreateUserAsync(prefix);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore connections = new(context);
        await connections.UpsertAuthorizationAsync(user.UserId, $"protected:tok-{prefix}", Scope, Now, Token);

        await new StudentProfileStore(context).UpsertAsync(
            user.UserId,
            "2025-2026",
            classYear,
            programLanguage,
            "0101240048",
            "1.0",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["practiceGroup"] = "A" },
            Now,
            Token);

        await connections.RequestInitialSyncAsync(user.UserId, Now.AddMinutes(1), Token);

        if (stage is Stage.Completed or Stage.CompletedThenReauth)
        {
            await connections.AttachManagedCalendarAsync(user.UserId, $"cal-{prefix}", Now.AddMinutes(2), Token);
            await connections.MarkInitialSyncCompletedAsync(user.UserId, Now.AddMinutes(3), Token);
        }

        if (stage is Stage.CompletedThenReauth)
        {
            await connections.MarkNeedsReauthorizationAsync(user.UserId, Now.AddMinutes(4), Token);
        }

        return user.UserId;
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
