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

    /// <summary>
    /// Records that Google rejected a user's credential during synchronization, moving the connection
    /// to <see cref="GoogleCalendarConnectionStatus.NeedsReauthorization"/> (ADR-059). Idempotent.
    /// </summary>
    Task MarkNeedsReauthorizationAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists authorized, initial-sync-completed connections that must replay semantic diffs
    /// missed while their credential was unavailable, oldest request first.
    /// </summary>
    Task<IReadOnlyList<PendingCalendarReconciliation>> ListPendingReconciliationAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Advances one user's durable semantic-diff replay cursor.</summary>
    Task AdvanceReconciliationCursorAsync(
        Guid userId,
        DateTimeOffset expectedRequiredSinceUtc,
        DateTimeOffset dispatchedAtUtc,
        Guid diffId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>Clears a reconciliation request after its bounded replay succeeds.</summary>
    Task CompleteReconciliationAsync(
        Guid userId,
        DateTimeOffset expectedRequiredSinceUtc,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>Records one successful non-destructive Calendar/ledger inventory pass.</summary>
    Task MarkCalendarInventoryCompletedAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the attached calendar unavailable so ordinary synchronization stops and the user
    /// sees an explicit repair-required state.
    /// </summary>
    Task MarkManagedCalendarUnavailableAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a user-initiated reconciliation request, making the connection due for the next
    /// non-destructive inventory pass. The worker does the actual work; this only records intent.
    /// </summary>
    Task<RequestReconciliationOutcome> RequestReconciliationAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

public enum RequestReconciliationOutcome
{
    /// <summary>The connection was made due; the worker will reconcile it on its next cycle.</summary>
    Requested,

    /// <summary>
    /// The connection cannot be reconciled on demand — it is not a healthy, initial-sync-completed
    /// connection with an available calendar (it may need re-authorization or repair).
    /// </summary>
    NotEligible,

    /// <summary>The user has no Calendar connection.</summary>
    NotFound,
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

    public DateTimeOffset? ManagedCalendarUnavailableAtUtc { get; init; }

    public DateTimeOffset? LastCalendarInventoryAtUtc { get; init; }

    /// <summary>Null when this connection has no missed-diff reconciliation to perform.</summary>
    public DateTimeOffset? ReconciliationRequiredSinceUtc { get; init; }

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

/// <summary>
/// A completed connection waiting to replay semantic diffs after re-authorization.
/// The encrypted credential is confined to this backend-only worker projection.
/// </summary>
public sealed record PendingCalendarReconciliation
{
    public required Guid UserId { get; init; }

    public required string ProtectedRefreshToken { get; init; }

    public required string ManagedCalendarId { get; init; }

    public required DateTimeOffset RequiredSinceUtc { get; init; }

    public required DateTimeOffset CursorDispatchedAtUtc { get; init; }

    public required Guid CursorDiffId { get; init; }
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
