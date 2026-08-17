using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// The worker-driven half of the Calendar connection boundary: the projections and state
/// transitions the synchronization services advance while converging a calendar. Consumed only by
/// the worker host; the API never calls these.
/// </summary>
public interface ICalendarSyncConnectionStore
    : IGoogleCalendarConnectionReader, ICalendarConnectionHealthWriter
{
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

    /// <summary>
    /// Lists connections whose student profile changed the audience their calendar resolves from,
    /// oldest request first (ADR-096). Like the other worker projections it carries the encrypted
    /// credential, because converging the calendar means writing to it.
    /// </summary>
    Task<IReadOnlyList<PendingProfileResync>> ListPendingProfileResyncAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clears a profile re-synchronization request after a complete pass converged the calendar,
    /// presenting the original request timestamp as an optimistic workflow token.
    /// </summary>
    Task<CompleteProfileResyncOutcome> CompleteProfileResyncAsync(
        Guid userId,
        DateTimeOffset expectedRequiredSinceUtc,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>Records one successful non-destructive Calendar/ledger inventory pass.</summary>
    Task MarkCalendarInventoryCompletedAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
