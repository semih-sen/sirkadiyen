using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Application.GoogleCalendar;

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

/// <summary>
/// A completed connection whose calendar must be converged onto a changed student profile
/// (ADR-096). The encrypted credential is confined to this backend-only worker projection.
/// </summary>
public sealed record PendingProfileResync
{
    public required Guid UserId { get; init; }

    public required string ProtectedRefreshToken { get; init; }

    public required string ManagedCalendarId { get; init; }

    /// <summary>The request timestamp, which is also its optimistic workflow token.</summary>
    public required DateTimeOffset RequiredSinceUtc { get; init; }
}

public enum CompleteProfileResyncOutcome
{
    /// <summary>The request this worker started was cleared.</summary>
    Completed,

    /// <summary>
    /// The profile changed again while the pass ran, so a newer request is pending and was left
    /// in place for the next cycle.
    /// </summary>
    Superseded,

    /// <summary>The user has no Calendar connection.</summary>
    NotFound,
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
