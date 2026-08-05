using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
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

    /// <summary>Whether the seeded user's access is currently active (ADR-095).</summary>
    private enum Access
    {
        Licensed,
        Revoked,
        NeverLicensed,
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

    [Fact]
    public async Task InventoryTargetsAreDueHealthyAndAlreadyCaughtUp()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid neverChecked = await SetUpAsync(
            "inventory-never",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed);
        Guid overdue = await SetUpAsync(
            "inventory-overdue",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed);
        Guid recent = await SetUpAsync(
            "inventory-recent",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed);
        Guid unavailable = await SetUpAsync(
            "inventory-unavailable",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed);
        Guid replayPending = await SetUpAsync(
            "inventory-replay",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed);

        await using (SirkadiyenDbContext mutate = fixture.CreateProductionLikeContext())
        {
            GoogleCalendarConnectionStore connections = new(mutate);
            await connections.MarkCalendarInventoryCompletedAsync(
                overdue,
                Now.AddDays(-2),
                Token);
            await connections.MarkCalendarInventoryCompletedAsync(recent, Now, Token);
            await connections.MarkManagedCalendarUnavailableAsync(
                unavailable,
                Now,
                Token);
            await connections.MarkNeedsReauthorizationAsync(replayPending, Now, Token);
            await connections.UpsertAuthorizationAsync(
                replayPending,
                "fresh",
                Scope,
                Now.AddMinutes(1),
                Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IReadOnlyList<CalendarInventoryTarget> targets =
            await new CalendarSyncTargetReadStore(context).ListInventoryTargetsAsync(
                Now.AddDays(-1),
                100,
                Token);

        Assert.Contains(targets, target => target.UserId == neverChecked);
        CalendarInventoryTarget due =
            Assert.Single(targets, target => target.UserId == overdue);
        Assert.Equal(Now.AddDays(-2), due.LastInventoryAtUtc);
        Assert.DoesNotContain(targets, target => target.UserId == recent);
        Assert.DoesNotContain(targets, target => target.UserId == unavailable);
        Assert.DoesNotContain(targets, target => target.UserId == replayPending);
    }

    [Fact]
    public async Task ARevokedOrUnlicensedUserReceivesNoFurtherCalendarWork()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid licensed = await SetUpAsync(
            "gate-licensed",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed);
        Guid revoked = await SetUpAsync(
            "gate-revoked",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed,
            Access.Revoked);
        Guid unlicensed = await SetUpAsync(
            "gate-unlicensed",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed,
            Access.NeverLicensed);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        CalendarSyncTargetReadStore store = new(context);

        IReadOnlyList<CalendarSyncTarget> cohort =
            await store.ListCohortTargetsAsync("2025-2026", 1, ProgramLanguage.Turkish, Token);
        IReadOnlyList<CalendarSyncTarget> byId =
            await store.ListTargetsByUserIdsAsync([licensed, revoked, unlicensed], Token);
        IReadOnlyList<CalendarInventoryTarget> inventory =
            await store.ListInventoryTargetsAsync(Now.AddDays(1), 500, Token);

        // Revocation stops future synchronization (ADR-022, ADR-095). Nothing here deletes an
        // event: the revoked student simply stops being chosen for diff fan-out, ledger-holder
        // updates and periodic inventory.
        Assert.Contains(cohort, target => target.UserId == licensed);
        Assert.DoesNotContain(cohort, target => target.UserId == revoked);
        Assert.DoesNotContain(cohort, target => target.UserId == unlicensed);

        Assert.Equal(licensed, Assert.Single(byId).UserId);

        Assert.Contains(inventory, target => target.UserId == licensed);
        Assert.DoesNotContain(inventory, target => target.UserId == revoked);
        Assert.DoesNotContain(inventory, target => target.UserId == unlicensed);
    }

    [Fact]
    public async Task RestoringAccessReadmitsTheUserOnTheNextRead()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        Guid user = await SetUpAsync(
            "gate-restored",
            1,
            ProgramLanguage.Turkish,
            Stage.Completed,
            Access.Revoked);

        UserSession admin = await CreateUserAsync("gate-restored-admin");
        await using (SirkadiyenDbContext mutate = fixture.CreateProductionLikeContext())
        {
            await new LicenseStore(mutate).ActivateManuallyAsync(
                user,
                admin.UserId,
                admin.Email,
                "Re-activated after the revocation was reversed.",
                Now.AddHours(1),
                Token);
        }

        await using SirkadiyenDbContext context = fixture.CreateContext();
        IReadOnlyList<CalendarSyncTarget> targets = await new CalendarSyncTargetReadStore(context)
            .ListTargetsByUserIdsAsync([user], Token);

        // No sweep is needed: the gate is a read-time predicate, so the next cycle simply admits
        // them again and the ordinary stages write whatever the ledger is missing.
        Assert.Equal(user, Assert.Single(targets).UserId);
    }

    private async Task<Guid> SetUpAsync(
        string prefix,
        int classYear,
        ProgramLanguage programLanguage,
        Stage stage,
        Access access = Access.Licensed)
    {
        UserSession user = await CreateUserAsync(prefix);
        await GrantAccessAsync(user.UserId, prefix, access);

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

    /// <summary>
    /// Gives the seeded user the access state the case needs. A manual activation is a
    /// <c>Redeemed</c> license with no code hash (ADR-053), which is what the gate reads.
    /// </summary>
    private async Task GrantAccessAsync(Guid userId, string prefix, Access access)
    {
        if (access is Access.NeverLicensed)
        {
            return;
        }

        UserSession admin = await CreateUserAsync($"{prefix}-admin");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        LicenseStore licenses = new(context);
        ManualLicenseActivationResult activation = await licenses.ActivateManuallyAsync(
            userId,
            admin.UserId,
            admin.Email,
            "Seeded by the test.",
            Now,
            Token);

        if (access is Access.Revoked)
        {
            await licenses.RevokeAsync(
                activation.LicenseId!.Value,
                admin.UserId,
                admin.Email,
                "Revoked by the test.",
                Now.AddMinutes(1),
                Token);
        }
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
