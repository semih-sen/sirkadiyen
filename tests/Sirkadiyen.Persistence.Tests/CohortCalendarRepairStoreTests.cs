using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;
using Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Proves the repair reads the cohort it was scoped to and flags only connections that can take
/// the flag (ADR-111).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CohortCalendarRepairStoreTests(PostgresFixture fixture)
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.app.created";

    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly CohortRepairScope Grade3Turkish = new()
    {
        AcademicYear = "2026-2027",
        ClassYear = 3,
        ProgramLanguage = ProgramLanguage.Turkish,
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HoldingsCoverTheScopedCohortWithTheLedgerRowsEachUserHolds()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid inScope = await SetUpAsync("repair-inscope", 3, ProgramLanguage.Turkish, complete: true);
        Guid otherYear = await SetUpAsync("repair-otheryear", 2, ProgramLanguage.Turkish, complete: true);
        // Initial sync unfinished: that stage computes the applicable set itself, so a repair
        // has nothing to converge for them.
        Guid inProgress = await SetUpAsync("repair-inprogress", 3, ProgramLanguage.Turkish, complete: false);

        await MapEventAsync(inScope, "identity-a");
        await MapEventAsync(inScope, "identity-b");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IReadOnlyList<CohortRepairHolding> holdings =
            await new CohortCalendarRepairStore(context)
                .ListCohortHoldingsAsync(Grade3Turkish, Token);

        CohortRepairHolding mine = Assert.Single(holdings, holding => holding.UserId == inScope);
        Assert.Equal(["identity-a", "identity-b"], mine.Mappings.Select(m => m.StableIdentity));
        Assert.Equal("3-A", mine.Profile.Selectors["curriculumGroup"]);
        Assert.DoesNotContain(holdings, holding => holding.UserId == otherYear);
        Assert.DoesNotContain(holdings, holding => holding.UserId == inProgress);
    }

    [Fact]
    public async Task AUserHoldingNothingIsStillPartOfTheCohort()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        // Their calendar may be missing events rather than holding surplus ones, and the plan
        // has to be able to say so.
        Guid empty = await SetUpAsync("repair-empty", 3, ProgramLanguage.Turkish, complete: true);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IReadOnlyList<CohortRepairHolding> holdings =
            await new CohortCalendarRepairStore(context)
                .ListCohortHoldingsAsync(Grade3Turkish, Token);

        Assert.Empty(Assert.Single(holdings, holding => holding.UserId == empty).Mappings);
    }

    [Fact]
    public async Task RequestingConvergenceFlagsOnlyConnectionsThatCanTakeItAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid ready = await SetUpAsync("repair-flag-ready", 3, ProgramLanguage.Turkish, complete: true);
        Guid inProgress = await SetUpAsync("repair-flag-progress", 3, ProgramLanguage.Turkish, complete: false);

        int requested;
        await using (SirkadiyenDbContext mutate = fixture.CreateProductionLikeContext())
        {
            requested = await new CohortCalendarRepairStore(mutate).RequestConvergenceAsync(
                [ready, inProgress, Guid.CreateVersion7()],
                Now.AddHours(1),
                Token);
        }

        Assert.Equal(1, requested);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Equal(
            Now.AddHours(1),
            await ResyncRequestedAtAsync(context, ready));
        Assert.Null(await ResyncRequestedAtAsync(context, inProgress));
    }

    [Fact]
    public async Task ARepairNeverPushesAnOlderUnconvergedRequestBackAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        // The queue is ordered by the oldest unconverged change. A repair that overwrote the
        // timestamp would send a student who already changed their profile to the back of it.
        Guid user = await SetUpAsync("repair-existing", 3, ProgramLanguage.Turkish, complete: true);

        await using (SirkadiyenDbContext first = fixture.CreateProductionLikeContext())
        {
            await new CohortCalendarRepairStore(first)
                .RequestConvergenceAsync([user], Now, Token);
        }

        await using (SirkadiyenDbContext second = fixture.CreateProductionLikeContext())
        {
            await new CohortCalendarRepairStore(second)
                .RequestConvergenceAsync([user], Now.AddHours(5), Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Equal(Now, await ResyncRequestedAtAsync(context, user));
    }

    private static async Task<DateTimeOffset?> ResyncRequestedAtAsync(
        SirkadiyenDbContext context,
        Guid userId) =>
        (await context.GoogleCalendarConnections
            .AsNoTracking()
            .SingleAsync(connection => connection.UserId == userId, Token))
        .ProfileResyncRequiredSinceUtc;

    private async Task MapEventAsync(Guid userId, string stableIdentity)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await new UserCalendarEventMappingStore(context).AddAsync(
            UserCalendarEventMapping.Create(
                userId,
                stableIdentity,
                SourceId.Parse("G3-TR-A-FACULTY"),
                Guid.CreateVersion7(),
                $"cal-{userId:N}",
                $"event-{stableIdentity}",
                "sha256:content",
                Now),
            Token);
    }

    private async Task<Guid> SetUpAsync(
        string prefix,
        int classYear,
        ProgramLanguage programLanguage,
        bool complete)
    {
        UserSession user = await CreateUserAsync(prefix);

        UserSession admin = await CreateUserAsync($"{prefix}-admin");
        await using (SirkadiyenDbContext licensing = fixture.CreateProductionLikeContext())
        {
            await new LicenseStore(licensing).ActivateManuallyAsync(
                user.UserId,
                admin.UserId,
                admin.Email,
                "Seeded by the test.",
                Now,
                Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        GoogleCalendarConnectionStore connections = new(context);
        await connections.UpsertAuthorizationAsync(
            user.UserId,
            $"protected:tok-{prefix}",
            Scope,
            Now,
            Token);

        await new StudentProfileStore(context).UpsertAsync(
            user.UserId,
            "2026-2027",
            classYear,
            programLanguage,
            "0101240048",
            "1.0",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["curriculumGroup"] = "3-A",
                ["facultyPracticeGroup"] = "A3",
            },
            Now,
            Token);

        await connections.RequestInitialSyncAsync(user.UserId, Now.AddMinutes(1), Token);

        if (complete)
        {
            await connections.AttachManagedCalendarAsync(
                user.UserId,
                $"cal-{user.UserId:N}",
                Now.AddMinutes(2),
                Token);
            await connections.MarkInitialSyncCompletedAsync(user.UserId, Now.AddMinutes(3), Token);
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
}
