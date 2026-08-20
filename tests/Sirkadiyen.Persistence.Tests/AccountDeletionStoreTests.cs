using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Domain.StudentProfiles;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Auditing.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// The transactional erasure in <see cref="AccountDeletionStore"/> (ADR-118): the personal
/// aggregates are cascaded away, the RESTRICT-bound tables are handled explicitly, the cross-cutting
/// audit trail is kept but anonymized, and no other account is touched.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountDeletionStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeletesPersonalDataAnonymizesTrailAndLeavesOtherAccountsAlone()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        UserSession admin = await CreateUserAsync("admin", UserRole.SuperAdmin);
        UserSession student = await CreateUserAsync("student", UserRole.User);
        UserSession other = await CreateUserAsync("other", UserRole.User);

        Guid licenseId;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            // A license created by the admin and redeemed by the student: this writes a license row
            // (RedeemedByUserId = student) and a RESTRICT-bound license-audit row (actor = student).
            byte[] hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
            License license = License.Create(hash, admin.UserId, admin.Email, Now, null, null);
            LicenseStore licenses = new(context);
            await licenses.SaveCreatedAsync(license, Token);
            await licenses.RedeemAsync(hash, student.UserId, student.Email, Now.AddMinutes(1), Token);
            licenseId = license.Id;
        }

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            context.StudentProfiles.Add(StudentProfile.Create(
                student.UserId,
                "2025-2026",
                1,
                ProgramLanguage.Turkish,
                "0102030405",
                "1.1",
                new Dictionary<string, string> { ["practiceGroup"] = "A" },
                Now));

            GoogleCalendarConnection connection = GoogleCalendarConnection.Create(
                student.UserId,
                "protected-refresh-token-ciphertext",
                "https://www.googleapis.com/auth/calendar",
                Now);
            connection.AttachManagedCalendar("student-calendar-id", Now);
            context.GoogleCalendarConnections.Add(connection);

            context.UserCalendarEventMappings.Add(UserCalendarEventMapping.Create(
                student.UserId,
                "lesson-1",
                SourceId.Parse("G1-TR-ANNUAL"),
                Guid.CreateVersion7(),
                "student-calendar-id",
                "event-1",
                "hash-1",
                Now));
            context.UserDepartmentColorPreferences.Add(UserDepartmentColorPreference.Create(
                student.UserId,
                "anatomi",
                "#123456",
                Now));

            // Another account's mapping, to prove isolation.
            context.UserCalendarEventMappings.Add(UserCalendarEventMapping.Create(
                other.UserId,
                "lesson-1",
                SourceId.Parse("G1-TR-ANNUAL"),
                Guid.CreateVersion7(),
                "other-calendar-id",
                "event-9",
                "hash-9",
                Now));

            await context.SaveChangesAsync(Token);
        }

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            AuditEventStore audit = new(context);
            // The student's own sign-in (actor = student) is anonymized.
            await audit.AppendAsync(SignIn(student), Token);
            // An operator action about the student (actor = admin, subject = student) is kept as-is.
            await audit.AppendAsync(OperatorActionAbout(student, admin), Token);
            // Another account's sign-in must be left untouched.
            await audit.AppendAsync(SignIn(other), Token);
        }

        AccountDeletionStoreResult result;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            result = await new AccountDeletionStore(context).DeleteAsync(
                student.UserId,
                AccountDeleted(student),
                Token);
        }

        Assert.True(result.Deleted);
        // The student's own sign-in and the AccountDeleted record just written = two anonymized rows.
        Assert.Equal(2, result.AnonymizedAuditEvents);
        Assert.Equal(1, result.DetachedLicenses);
        Assert.Equal(1, result.DeletedLicenseAudits);

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            // The personal aggregates are gone; the other account keeps everything.
            Assert.Null(await context.Users.FindAsync([student.UserId], Token));
            Assert.NotNull(await context.Users.FindAsync([other.UserId], Token));
            Assert.NotNull(await context.Users.FindAsync([admin.UserId], Token));
            Assert.False(await context.StudentProfiles
                .AnyAsync(profile => profile.UserId == student.UserId, Token));
            Assert.False(await context.GoogleCalendarConnections
                .AnyAsync(connection => connection.UserId == student.UserId, Token));
            Assert.False(await context.UserCalendarEventMappings
                .AnyAsync(mapping => mapping.UserId == student.UserId, Token));
            Assert.False(await context.UserDepartmentColorPreferences
                .AnyAsync(preference => preference.UserId == student.UserId, Token));
            Assert.True(await context.UserCalendarEventMappings
                .AnyAsync(mapping => mapping.UserId == other.UserId, Token));

            // The license row survives, detached from the deleted redeemer but still Redeemed.
            License? license = await context.Licenses.FindAsync([licenseId], Token);
            Assert.NotNull(license);
            Assert.Null(license!.RedeemedByUserId);
            Assert.Equal(LicenseStatus.Redeemed, license.Status);

            // The student's own license-audit row is gone; the admin's creation audit remains.
            Assert.False(await context.LicenseAudits
                .AnyAsync(a => a.ActorUserId == student.UserId, Token));
            Assert.True(await context.LicenseAudits
                .AnyAsync(a => a.ActorUserId == admin.UserId, Token));

            // The AccountDeleted record persists, but its self-actor is anonymized away.
            AuditEvent accountDeleted = await context.AuditEvents
                .SingleAsync(
                    e => e.Category == AuditEventCategory.AccountDeleted
                        && e.SubjectId == student.UserId.ToString(),
                    Token);
            Assert.Null(accountDeleted.ActorUserId);
            Assert.Null(accountDeleted.ActorEmail);
            Assert.Equal(student.UserId.ToString(), accountDeleted.SubjectId);

            // No audit row anywhere still names the deleted student as actor.
            Assert.False(await context.AuditEvents
                .AnyAsync(e => e.ActorUserId == student.UserId, Token));

            // The operator action about the student keeps its (admin) actor and subject reference.
            AuditEvent operatorAction = await context.AuditEvents
                .SingleAsync(
                    e => e.Category == AuditEventCategory.CalendarRepairRequested
                        && e.SubjectId == student.UserId.ToString(),
                    Token);
            Assert.Equal(admin.UserId, operatorAction.ActorUserId);
            Assert.Equal(admin.Email, operatorAction.ActorEmail);

            // The other account's sign-in is untouched.
            Assert.True(await context.AuditEvents
                .AnyAsync(e => e.ActorUserId == other.UserId, Token));
        }
    }

    [Fact]
    public async Task ReportsAMissingUserWithoutChangingAnything()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        AccountDeletionStoreResult result = await new AccountDeletionStore(context).DeleteAsync(
            Guid.CreateVersion7(),
            AccountDeleted(new UserSession
            {
                UserId = Guid.CreateVersion7(),
                Email = "ghost@example.com",
                Role = UserRole.User,
                LastSignedInAtUtc = Now,
            }),
            Token);

        Assert.False(result.Deleted);
    }

    private static AuditEvent SignIn(UserSession user) => AuditEvent.Create(
        AuditEventCategory.SignIn,
        Now,
        user.UserId,
        user.Email,
        null,
        null,
        "corr",
        "203.0.113.0",
        "enc:203.0.113.7",
        "agent",
        null,
        null);

    private static AuditEvent OperatorActionAbout(UserSession subject, UserSession actor) =>
        AuditEvent.Create(
            AuditEventCategory.CalendarRepairRequested,
            Now,
            actor.UserId,
            actor.Email,
            "User",
            subject.UserId.ToString(),
            "corr",
            null,
            null,
            null,
            "cohort repair",
            null);

    private static AuditEvent AccountDeleted(UserSession subject) => AuditEvent.Create(
        AuditEventCategory.AccountDeleted,
        Now.AddMinutes(5),
        subject.UserId,
        subject.Email,
        "User",
        subject.UserId.ToString(),
        "corr",
        null,
        null,
        null,
        null,
        "{\"requestedBy\":\"self\"}");

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
                DisplayName = prefix,
            },
            role,
            Now,
            Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
