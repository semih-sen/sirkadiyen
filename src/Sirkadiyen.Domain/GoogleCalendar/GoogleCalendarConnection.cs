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
    /// null until initial sync creates the calendar, which is a later phase; a
    /// connection is authorized long before it has a calendar.
    /// </summary>
    public string? ManagedCalendarId { get; private set; }

    public GoogleCalendarConnectionStatus Status { get; private set; }

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
    /// The managed calendar is deliberately preserved: re-granting access must not orphan
    /// the calendar the user's events already live in (ADR-024).
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
