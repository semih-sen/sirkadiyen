using Sirkadiyen.Domain.Identity;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VerifiedGoogleIdentityCreatesInactiveLocalUserFoundation()
    {
        User user = User.CreateFromGoogle(
            "google-subject-1",
            " Student@Example.com ",
            isEmailVerified: true,
            " Student Name ",
            UserRole.User,
            Now);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("google-subject-1", user.GoogleSubject);
        Assert.Equal("Student@Example.com", user.Email);
        Assert.Equal("STUDENT@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("Student Name", user.DisplayName);
        Assert.True(user.IsEmailVerified);
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal(Now, user.CreatedAtUtc);
        Assert.Equal(Now, user.LastSignedInAtUtc);
    }

    [Fact]
    public void UnverifiedGoogleEmailCannotCreateOrRefreshAUser()
    {
        Assert.Throws<ArgumentException>(() => User.CreateFromGoogle(
            "google-subject-1",
            "student@example.com",
            isEmailVerified: false,
            null,
            UserRole.User,
            Now));

        User user = User.CreateFromGoogle(
            "google-subject-1",
            "student@example.com",
            isEmailVerified: true,
            null,
            UserRole.User,
            Now);

        Assert.Throws<ArgumentException>(() => user.RegisterVerifiedGoogleSignIn(
            "student@example.com",
            isEmailVerified: false,
            null,
            Now.AddHours(1)));
    }

    [Fact]
    public void LaterVerifiedSignInRefreshesProfileButDoesNotChangeGoogleSubject()
    {
        User user = User.CreateFromGoogle(
            "stable-google-subject",
            "old@example.com",
            isEmailVerified: true,
            "Old Name",
            UserRole.User,
            Now);

        user.RegisterVerifiedGoogleSignIn(
            "new@example.com",
            isEmailVerified: true,
            "New Name",
            Now.AddHours(1));

        Assert.Equal("stable-google-subject", user.GoogleSubject);
        Assert.Equal("new@example.com", user.Email);
        Assert.Equal("NEW@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("New Name", user.DisplayName);
        Assert.Equal(Now.AddHours(1), user.LastSignedInAtUtc);
    }

    [Fact]
    public void BootstrapCanGrantSuperAdminButCannotDemoteIt()
    {
        User user = User.CreateFromGoogle(
            "google-subject-1",
            "admin@example.com",
            isEmailVerified: true,
            null,
            UserRole.User,
            Now);

        user.GrantRole(UserRole.SuperAdmin, Now.AddMinutes(1));
        user.GrantRole(UserRole.User, Now.AddMinutes(2));

        Assert.Equal(UserRole.SuperAdmin, user.Role);
        Assert.Equal(Now.AddMinutes(1), user.UpdatedAtUtc);
    }
}
