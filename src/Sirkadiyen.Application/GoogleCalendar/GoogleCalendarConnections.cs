using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>Persistence boundary for the single Calendar connection a user owns.</summary>
public interface IGoogleCalendarConnectionStore
{
    Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Records an authorization, replacing any existing one, transactionally.</summary>
    Task<GoogleCalendarConnectionView> UpsertAuthorizationAsync(
        Guid userId,
        string protectedRefreshToken,
        string grantedScopes,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records that the user asked to begin their one-time initial synchronization, moving an
    /// authorized connection to <see cref="GoogleCalendarInitialSyncState.InProgress"/> so the
    /// worker will act on it (ADR-058).
    /// </summary>
    Task<RequestInitialSyncResult> RequestInitialSyncAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists connections whose initial synchronization is in progress, oldest first, for the
    /// worker to act on. Carries the stored ciphertext credential because the sync path is the
    /// one place that legitimately needs it; it never leaves the backend.
    /// </summary>
    Task<IReadOnlyList<PendingCalendarSync>> ListPendingInitialSyncAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Attaches the dedicated calendar created during initial sync (ADR-024).</summary>
    Task AttachManagedCalendarAsync(
        Guid userId,
        string managedCalendarId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>Marks the user's initial synchronization finished.</summary>
    Task MarkInitialSyncCompletedAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// A read projection of a stored connection. It deliberately excludes the stored
/// credential: nothing outside the synchronization path needs it, and an API response
/// must never be able to carry it by accident.
/// </summary>
public sealed record GoogleCalendarConnectionView
{
    public required Guid UserId { get; init; }

    public required string GrantedScopes { get; init; }

    public required GoogleCalendarConnectionStatus Status { get; init; }

    /// <summary>How far the one-time initial synchronization has progressed (ADR-058).</summary>
    public required GoogleCalendarInitialSyncState InitialSyncState { get; init; }

    /// <summary>Null until initial sync creates the dedicated calendar (ADR-024).</summary>
    public string? ManagedCalendarId { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>
/// A connection the worker must run initial synchronization for. Unlike
/// <see cref="GoogleCalendarConnectionView"/> this carries the ciphertext refresh token,
/// because creating the calendar and inserting events needs the credential.
/// </summary>
public sealed record PendingCalendarSync
{
    public required Guid UserId { get; init; }

    public required string ProtectedRefreshToken { get; init; }

    /// <summary>Set once the calendar has been created; null on the first pass.</summary>
    public string? ManagedCalendarId { get; init; }
}

public sealed record RequestInitialSyncResult
{
    public required RequestInitialSyncOutcome Outcome { get; init; }

    public GoogleCalendarConnectionView? Connection { get; init; }
}

public enum RequestInitialSyncOutcome
{
    /// <summary>The connection moved to in-progress; the worker will pick it up.</summary>
    Requested,

    /// <summary>Synchronization was already under way; the request changed nothing.</summary>
    AlreadyInProgress,

    /// <summary>Synchronization was already finished; the request changed nothing.</summary>
    AlreadyCompleted,

    /// <summary>The connection needs re-authorization, so it cannot synchronize yet.</summary>
    NotAuthorized,

    /// <summary>The user has no Calendar connection to synchronize.</summary>
    NotFound,
}

/// <summary>
/// Protects the Google refresh token at rest. Implemented in the infrastructure layer so
/// no cryptographic provider leaks into the domain or the use cases (ADR-057).
/// </summary>
public interface ICalendarTokenProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
