using Sirkadiyen.Domain.GoogleCalendar;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class GoogleCalendarConnectionTests
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.app.created";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void CreateStartsAuthorizedAndTrimsTheStoredValues()
    {
        GoogleCalendarConnection connection = GoogleCalendarConnection.Create(
            UserId,
            "  protected-token  ",
            $" {Scope} ",
            Now);

        Assert.NotEqual(Guid.Empty, connection.Id);
        Assert.Equal(UserId, connection.UserId);
        Assert.Equal("protected-token", connection.ProtectedRefreshToken);
        Assert.Equal(Scope, connection.GrantedScopes);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
        Assert.Equal(Now, connection.CreatedAtUtc);
        Assert.Equal(Now, connection.UpdatedAtUtc);
    }

    [Fact]
    public void ANewConnectionHasNoManagedCalendarYet()
    {
        // The dedicated calendar is created by initial sync, not by authorization
        // (ADR-024), so a freshly authorized connection must not claim one.
        GoogleCalendarConnection connection = Create();

        Assert.Null(connection.ManagedCalendarId);
    }

    [Fact]
    public void ReauthorizingReplacesTheCredentialAndKeepsIdentity()
    {
        GoogleCalendarConnection connection = Create();
        Guid id = connection.Id;

        connection.Reauthorize("second-token", Scope, Now.AddDays(30));

        Assert.Equal(id, connection.Id);
        Assert.Equal(UserId, connection.UserId);
        Assert.Equal("second-token", connection.ProtectedRefreshToken);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
        Assert.Equal(Now, connection.CreatedAtUtc);
        Assert.Equal(Now.AddDays(30), connection.UpdatedAtUtc);
    }

    [Fact]
    public void AConnectionMustHaveAnOwner() =>
        Assert.Throws<ArgumentException>(() => Create(userId: Guid.Empty));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCredentialOrScopeIsRejected(string blank)
    {
        Assert.Throws<ArgumentException>(() => Create(protectedRefreshToken: blank));
        Assert.Throws<ArgumentException>(() => Create(grantedScopes: blank));
    }

    [Fact]
    public void ACredentialLongerThanTheBoundIsRejected()
    {
        string tooLong = new(
            'x',
            GoogleCalendarConnection.MaximumProtectedRefreshTokenLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(protectedRefreshToken: tooLong));
    }

    [Fact]
    public void CreateStartsWithInitialSyncPending() =>
        Assert.Equal(GoogleCalendarInitialSyncState.Pending, Create().InitialSyncState);

    [Fact]
    public void RequestingInitialSyncMovesAPendingConnectionToInProgress()
    {
        GoogleCalendarConnection connection = Create();

        connection.RequestInitialSync(Now.AddMinutes(1));

        Assert.Equal(GoogleCalendarInitialSyncState.InProgress, connection.InitialSyncState);
        Assert.Equal(Now.AddMinutes(1), connection.UpdatedAtUtc);
    }

    [Fact]
    public void RequestingInitialSyncTwiceIsRejected()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now);

        Assert.Throws<InvalidOperationException>(() => connection.RequestInitialSync(Now));
    }

    [Fact]
    public void AManagedCalendarIsAttachedExactlyOnce()
    {
        GoogleCalendarConnection connection = Create();

        connection.AttachManagedCalendar("calendar-id", Now.AddMinutes(1));
        Assert.Equal("calendar-id", connection.ManagedCalendarId);

        // The calendar the user's events already live in must not be silently replaced.
        Assert.Throws<InvalidOperationException>(
            () => connection.AttachManagedCalendar("another-id", Now.AddMinutes(2)));
    }

    [Fact]
    public void InitialSyncCannotCompleteBeforeItStarts() =>
        Assert.Throws<InvalidOperationException>(() => Create().CompleteInitialSync(Now));

    [Fact]
    public void InitialSyncCannotCompleteWithoutACalendar()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now);

        Assert.Throws<InvalidOperationException>(() => connection.CompleteInitialSync(Now));
    }

    [Fact]
    public void InitialSyncCompletesOnceStartedAndGivenACalendar()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now.AddMinutes(1));
        connection.AttachManagedCalendar("calendar-id", Now.AddMinutes(2));

        connection.CompleteInitialSync(Now.AddMinutes(3));

        Assert.Equal(GoogleCalendarInitialSyncState.Completed, connection.InitialSyncState);
        Assert.Equal(Now.AddMinutes(3), connection.UpdatedAtUtc);
    }

    [Fact]
    public void ReauthorizingPreservesTheCalendarAndInitialSyncProgress()
    {
        GoogleCalendarConnection connection = Create();
        connection.RequestInitialSync(Now.AddMinutes(1));
        connection.AttachManagedCalendar("calendar-id", Now.AddMinutes(2));
        connection.CompleteInitialSync(Now.AddMinutes(3));

        connection.Reauthorize("fresh-token", Scope, Now.AddDays(30));

        // A user who re-grants access keeps their calendar and does not re-run initial sync.
        Assert.Equal("calendar-id", connection.ManagedCalendarId);
        Assert.Equal(GoogleCalendarInitialSyncState.Completed, connection.InitialSyncState);
        Assert.Equal(GoogleCalendarConnectionStatus.Authorized, connection.Status);
    }

    private static GoogleCalendarConnection Create(
        Guid? userId = null,
        string protectedRefreshToken = "protected-token",
        string grantedScopes = Scope) =>
        GoogleCalendarConnection.Create(
            userId ?? UserId,
            protectedRefreshToken,
            grantedScopes,
            Now);
}
