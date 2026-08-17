using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.GoogleCalendar;
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
        Assert.Equal(1, item.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, item.ProgramLanguage);
        Assert.Equal("0102030405", item.StudentNumber);
        Assert.Equal(0, item.ManagedEventCount);
        Assert.Null(item.CalendarStatus);
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
        Assert.Null(detail.CalendarConnection);
    }

    [Fact]
    public async Task DetailProjectsTheCalendarConnectionWithoutTheCredential()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession student = await CreateUserAsync("student-connection", UserRole.User);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await AddConnectionAsync(context, student.UserId);

        AdminUserDetail? detail = await new AdminUserReadStore(context)
            .FindAsync(student.UserId, Token);

        Assert.NotNull(detail);
        AdminUserCalendarConnection connection = Assert.IsType<AdminUserCalendarConnection>(
            detail!.CalendarConnection);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
        Assert.Equal(GoogleCalendarInitialSyncState.Pending, connection.InitialSyncState);
        Assert.False(connection.HasManagedCalendar);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, detail.Summary.CalendarStatus);
        Assert.Equal(
            GoogleCalendarInitialSyncState.Pending,
            detail.Summary.InitialSyncState);
    }

    [Fact]
    public async Task SearchMatchesTheDisplayNameAndTheStudentNumberPrefix()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession student = await CreateUserAsync(
            "student-search",
            UserRole.User,
            displayName: "Zeynep Kavaklıoğlu");

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await AddProfileAsync(context, student.UserId, studentNumber: "1234567890");

        AdminUserReadStore store = new(context);

        PagedResult<AdminUserListItem> byName = await store.ListAsync(
            new AdminUserQuery { Search = "kavaklıoğlu" },
            Token);
        Assert.Contains(byName.Items, item => item.Id == student.UserId);

        PagedResult<AdminUserListItem> byNumber = await store.ListAsync(
            new AdminUserQuery { Search = "123456" },
            Token);
        Assert.Contains(byNumber.Items, item => item.Id == student.UserId);

        // A LIKE wildcard typed into the box is a literal, not a pattern.
        PagedResult<AdminUserListItem> byWildcard = await store.ListAsync(
            new AdminUserQuery { Search = "%" },
            Token);
        Assert.DoesNotContain(byWildcard.Items, item => item.Id == student.UserId);
    }

    [Fact]
    public async Task ProfileFiltersNarrowToTheAskedCohort()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession first = await CreateUserAsync("cohort-a", UserRole.User);
        UserSession second = await CreateUserAsync("cohort-b", UserRole.User);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await AddProfileAsync(
            context,
            first.UserId,
            classYear: 2,
            selectors: new Dictionary<string, string>
            {
                ["practiceGroup"] = "A",
                ["anatomyGroup"] = "2",
            });
        await AddProfileAsync(
            context,
            second.UserId,
            classYear: 2,
            selectors: new Dictionary<string, string>
            {
                ["practiceGroup"] = "B",
                ["anatomyGroup"] = "2",
            });

        AdminUserReadStore store = new(context);

        PagedResult<AdminUserListItem> byClassYear = await store.ListAsync(
            new AdminUserQuery { ClassYear = 2, ProgramLanguage = ProgramLanguage.Turkish },
            Token);
        Assert.Contains(byClassYear.Items, item => item.Id == first.UserId);
        Assert.Contains(byClassYear.Items, item => item.Id == second.UserId);

        // Every requested selector must match, so the shared anatomy group does not widen it back.
        PagedResult<AdminUserListItem> bySelectors = await store.ListAsync(
            new AdminUserQuery
            {
                ClassYear = 2,
                Selectors = new Dictionary<string, string>
                {
                    ["practiceGroup"] = "A",
                    ["anatomyGroup"] = "2",
                },
            },
            Token);
        Assert.Equal(first.UserId, Assert.Single(bySelectors.Items).Id);
        Assert.Equal(1, bySelectors.TotalCount);

        PagedResult<AdminUserListItem> noMatch = await store.ListAsync(
            new AdminUserQuery
            {
                ClassYear = 2,
                Selectors = new Dictionary<string, string> { ["practiceGroup"] = "H" },
            },
            Token);
        Assert.Empty(noMatch.Items);
    }

    [Fact]
    public async Task LicenseStateFilterSeparatesActiveFromSuspendedAndUnlicensed()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession admin = await CreateUserAsync("admin-license-filter", UserRole.SuperAdmin);
        UserSession active = await CreateUserAsync("licensed-active", UserRole.User);
        UserSession suspended = await CreateUserAsync("licensed-suspended", UserRole.User);
        UserSession none = await CreateUserAsync("licensed-none", UserRole.User);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await ActivateAsync(context, admin, active);
        Guid revokedLicenseId = await ActivateAsync(context, admin, suspended);
        await new LicenseStore(context).RevokeAsync(
            revokedLicenseId,
            admin.UserId,
            admin.Email,
            "Test",
            Now.AddMinutes(2),
            Token);

        AdminUserReadStore store = new(context);

        PagedResult<AdminUserListItem> activePage = await store.ListAsync(
            new AdminUserQuery { LicenseState = UserLicenseState.Active, PageSize = 200 },
            Token);
        Assert.Contains(activePage.Items, item => item.Id == active.UserId);
        Assert.DoesNotContain(activePage.Items, item => item.Id == suspended.UserId);
        Assert.DoesNotContain(activePage.Items, item => item.Id == none.UserId);

        PagedResult<AdminUserListItem> suspendedPage = await store.ListAsync(
            new AdminUserQuery { LicenseState = UserLicenseState.Suspended, PageSize = 200 },
            Token);
        Assert.Contains(suspendedPage.Items, item => item.Id == suspended.UserId);
        Assert.DoesNotContain(suspendedPage.Items, item => item.Id == active.UserId);

        PagedResult<AdminUserListItem> nonePage = await store.ListAsync(
            new AdminUserQuery { LicenseState = UserLicenseState.None, PageSize = 200 },
            Token);
        Assert.Contains(nonePage.Items, item => item.Id == none.UserId);
        Assert.DoesNotContain(nonePage.Items, item => item.Id == suspended.UserId);
    }

    [Fact]
    public async Task CalendarFiltersSelectByConnectionPresenceAndSyncState()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession connected = await CreateUserAsync("calendar-connected", UserRole.User);
        UserSession unconnected = await CreateUserAsync("calendar-unconnected", UserRole.User);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        await AddConnectionAsync(context, connected.UserId);

        AdminUserReadStore store = new(context);

        PagedResult<AdminUserListItem> withConnection = await store.ListAsync(
            new AdminUserQuery { HasCalendarConnection = true, PageSize = 200 },
            Token);
        Assert.Contains(withConnection.Items, item => item.Id == connected.UserId);
        Assert.DoesNotContain(withConnection.Items, item => item.Id == unconnected.UserId);

        PagedResult<AdminUserListItem> withoutConnection = await store.ListAsync(
            new AdminUserQuery { HasCalendarConnection = false, PageSize = 200 },
            Token);
        Assert.Contains(withoutConnection.Items, item => item.Id == unconnected.UserId);

        PagedResult<AdminUserListItem> completed = await store.ListAsync(
            new AdminUserQuery
            {
                InitialSyncState = GoogleCalendarInitialSyncState.Completed,
                PageSize = 200,
            },
            Token);
        Assert.DoesNotContain(completed.Items, item => item.Id == connected.UserId);
    }

    [Fact]
    public async Task SortingByEmailIsStableAndReversible()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await CreateUserAsync("sorted", UserRole.User);
        await CreateUserAsync("sorted", UserRole.User);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AdminUserReadStore store = new(context);

        PagedResult<AdminUserListItem> ascending = await store.ListAsync(
            new AdminUserQuery { Sort = AdminUserSort.Email, Descending = false, PageSize = 200 },
            Token);
        PagedResult<AdminUserListItem> descending = await store.ListAsync(
            new AdminUserQuery { Sort = AdminUserSort.Email, Descending = true, PageSize = 200 },
            Token);

        Assert.Equal(
            ascending.Items.Select(item => item.Email),
            descending.Items.Select(item => item.Email).Reverse());
    }

    [Fact]
    public async Task UnknownUserDetailIsNull()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Null(await new AdminUserReadStore(context).FindAsync(Guid.CreateVersion7(), Token));
    }

    private static async Task<Guid> ActivateAsync(
        SirkadiyenDbContext context,
        UserSession admin,
        UserSession student)
    {
        byte[] hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        License license = License.Create(hash, admin.UserId, admin.Email, Now, null, null);
        LicenseStore store = new(context);
        await store.SaveCreatedAsync(license, Token);
        await store.RedeemAsync(hash, student.UserId, student.Email, Now.AddMinutes(1), Token);
        return license.Id;
    }

    private static async Task AddProfileAsync(
        SirkadiyenDbContext context,
        Guid userId,
        int classYear = 1,
        string studentNumber = "0102030405",
        IReadOnlyDictionary<string, string>? selectors = null)
    {
        context.StudentProfiles.Add(StudentProfile.Create(
            userId,
            "2025-2026",
            classYear,
            ProgramLanguage.Turkish,
            studentNumber,
            "1.1",
            selectors ?? new Dictionary<string, string> { ["practiceGroup"] = "A" },
            Now));
        await context.SaveChangesAsync(Token);
    }

    private static async Task AddConnectionAsync(SirkadiyenDbContext context, Guid userId)
    {
        context.GoogleCalendarConnections.Add(GoogleCalendarConnection.Create(
            userId,
            "protected-refresh-token-ciphertext",
            "https://www.googleapis.com/auth/calendar",
            Now));
        await context.SaveChangesAsync(Token);
    }

    private async Task<UserSession> CreateUserAsync(
        string prefix,
        UserRole role,
        string? displayName = null)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        string nonce = Guid.NewGuid().ToString("N");
        return await new UserStore(context).SignInWithGoogleAsync(
            new GoogleIdentity
            {
                Subject = $"{prefix}-{nonce}",
                Email = $"{prefix}-{nonce}@example.com",
                EmailVerified = true,
                DisplayName = displayName,
            },
            role,
            Now,
            Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
