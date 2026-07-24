namespace Sirkadiyen.Domain.GoogleCalendar;

/// <summary>
/// A user's standing authorization for Sirkadiyen to manage their Google Calendar,
/// holding the long-lived refresh token that later makes unattended synchronization
/// possible.
/// </summary>
/// <remarks>
/// The refresh token is a credential and is never held here in plaintext: the
/// application layer protects it before the aggregate is constructed, so this type
/// stores an opaque ciphertext string and the domain keeps no dependency on any
/// cryptographic provider (ADR-057).
/// <para>
/// The aggregate enforces only structural invariants. Whether the granted scopes are
/// <em>sufficient</em> is judged in the application layer against the scope the
/// product requires, which is policy that can change without a schema migration.
/// </para>
/// </remarks>
public sealed class GoogleCalendarConnection
{
    /// <summary>Generous bound: the ciphertext is far longer than the token itself.</summary>
    public const int MaximumProtectedRefreshTokenLength = 8192;

    public const int MaximumGrantedScopesLength = 2000;

    public const int MaximumManagedCalendarIdLength = 1024;

    private GoogleCalendarConnection()
    {
        // Materialization constructor.
        ProtectedRefreshToken = string.Empty;
        GrantedScopes = string.Empty;
    }

    public Guid Id { get; private init; }

    public Guid UserId { get; private init; }

    /// <summary>The Google refresh token, already encrypted by the application layer.</summary>
    public string ProtectedRefreshToken { get; private set; }

    /// <summary>The scopes Google reported as actually granted, space-delimited.</summary>
    public string GrantedScopes { get; private set; }

    /// <summary>
    /// The dedicated Sirkadiyen calendar this connection writes to (ADR-024). It stays
    /// null until initial sync creates the calendar; a connection is authorized long
    /// before it has a calendar.
    /// </summary>
    public string? ManagedCalendarId { get; private set; }

    public GoogleCalendarConnectionStatus Status { get; private set; }

    /// <summary>
    /// How far the one-time initial synchronization has progressed (ADR-058). It is
    /// orthogonal to <see cref="Status"/>: authorization says whether the credential
    /// works, this says whether the user's calendar has been populated yet.
    /// </summary>
    public GoogleCalendarInitialSyncState InitialSyncState { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    /// <summary>When the current grant was recorded; a re-authorization advances it.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    public static GoogleCalendarConnection Create(
        Guid userId,
        string protectedRefreshToken,
        string grantedScopes,
        DateTimeOffset atUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A connection owner is required.", nameof(userId));
        }

        return new GoogleCalendarConnection
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ProtectedRefreshToken = RequiredBounded(
                protectedRefreshToken,
                MaximumProtectedRefreshTokenLength,
                nameof(protectedRefreshToken)),
            GrantedScopes = RequiredBounded(
                grantedScopes,
                MaximumGrantedScopesLength,
                nameof(grantedScopes)),
            Status = GoogleCalendarConnectionStatus.Authorized,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };
    }

    /// <summary>
    /// Records a fresh authorization over an existing connection, replacing the stored
    /// credential and restoring the authorized status.
    /// </summary>
    /// <remarks>
    /// The managed calendar and the initial-sync progress are deliberately preserved:
    /// re-granting access must not orphan the calendar the user's events already live in
    /// (ADR-024), nor make an already-synchronized user repeat their initial sync.
    /// </remarks>
    public void Reauthorize(
        string protectedRefreshToken,
        string grantedScopes,
        DateTimeOffset atUtc)
    {
        ProtectedRefreshToken = RequiredBounded(
            protectedRefreshToken,
            MaximumProtectedRefreshTokenLength,
            nameof(protectedRefreshToken));
        GrantedScopes = RequiredBounded(
            grantedScopes,
            MaximumGrantedScopesLength,
            nameof(grantedScopes));
        Status = GoogleCalendarConnectionStatus.Authorized;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Records that the user asked to begin their initial synchronization, moving the
    /// connection from <see cref="GoogleCalendarInitialSyncState.Pending"/> to
    /// <see cref="GoogleCalendarInitialSyncState.InProgress"/> so the worker will act on it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The credential is not authorized, or synchronization is already under way or done.
    /// </exception>
    public void RequestInitialSync(DateTimeOffset atUtc)
    {
        if (Status is not GoogleCalendarConnectionStatus.Authorized)
        {
            throw new InvalidOperationException(
                "Initial synchronization cannot start while the connection needs re-authorization.");
        }

        if (InitialSyncState is not GoogleCalendarInitialSyncState.Pending)
        {
            throw new InvalidOperationException(
                $"Initial synchronization has already reached {InitialSyncState}.");
        }

        InitialSyncState = GoogleCalendarInitialSyncState.InProgress;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Attaches the dedicated calendar created during initial sync (ADR-024). It is set
    /// exactly once; the calendar the user's events live in is never silently replaced.
    /// </summary>
    /// <exception cref="InvalidOperationException">A calendar is already attached.</exception>
    public void AttachManagedCalendar(string managedCalendarId, DateTimeOffset atUtc)
    {
        if (ManagedCalendarId is not null)
        {
            throw new InvalidOperationException(
                "A managed calendar is already attached to this connection.");
        }

        ManagedCalendarId = RequiredBounded(
            managedCalendarId,
            MaximumManagedCalendarIdLength,
            nameof(managedCalendarId));
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Marks the initial synchronization finished, moving the connection to
    /// <see cref="GoogleCalendarInitialSyncState.Completed"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Synchronization is not in progress, or no calendar was ever attached.
    /// </exception>
    public void CompleteInitialSync(DateTimeOffset atUtc)
    {
        if (InitialSyncState is not GoogleCalendarInitialSyncState.InProgress)
        {
            throw new InvalidOperationException(
                $"Initial synchronization cannot complete from {InitialSyncState}.");
        }

        if (ManagedCalendarId is null)
        {
            throw new InvalidOperationException(
                "Initial synchronization cannot complete before a calendar is attached.");
        }

        InitialSyncState = GoogleCalendarInitialSyncState.Completed;
        UpdatedAtUtc = atUtc;
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }
}

public enum GoogleCalendarConnectionStatus
{
    /// <summary>The stored refresh token is expected to work.</summary>
    Authorized,

    /// <summary>
    /// Google rejected the stored credential; synchronization must stop until the user
    /// grants access again. Only synchronization sets this, so nothing produces it yet.
    /// </summary>
    NeedsReauthorization,
}

/// <summary>
/// How far the one-time initial synchronization has progressed for a connection (ADR-058).
/// </summary>
public enum GoogleCalendarInitialSyncState
{
    /// <summary>Authorized, but the user has not asked to populate their calendar yet.</summary>
    Pending,

    /// <summary>The user asked to start; the worker is creating the calendar and writing events.</summary>
    InProgress,

    /// <summary>Every currently-published event that applies to the user has been written.</summary>
    Completed,
}
