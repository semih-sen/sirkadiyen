using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// The user-driven half of the Calendar connection boundary: authorizing, and recording the intent
/// to synchronize or reconcile. Consumed by the API host (authorization, the sync and reconcile
/// endpoints); the worker never calls these.
/// </summary>
public interface IUserCalendarConnectionStore : IGoogleCalendarConnectionReader
{
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
    /// Records a user-initiated reconciliation request, making the connection due for the next
    /// non-destructive inventory pass. The worker does the actual work; this only records intent.
    /// </summary>
    Task<RequestReconciliationOutcome> RequestReconciliationAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
