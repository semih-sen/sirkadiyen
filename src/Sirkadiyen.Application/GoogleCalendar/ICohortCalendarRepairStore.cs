namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// The API-side half of a cohort calendar repair (ADR-111): what each user in a program currently
/// holds, and the flag that asks the worker to converge them.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ICalendarSyncTargetReadStore"/>, which carries the
/// encrypted refresh token because the worker writes calendars with it. Planning a repair needs
/// only profiles and ledger rows, so a credential never reaches the request path
/// (AI_GUIDELINE §15).
/// </remarks>
public interface ICohortCalendarRepairStore
{
    /// <summary>
    /// Every synchronization-ready user in the program with the lessons their calendar holds,
    /// ordered by user id so a plan — and the hash over it — is deterministic.
    /// </summary>
    Task<IReadOnlyList<CohortRepairHolding>> ListCohortHoldingsAsync(
        CohortRepairScope scope,
        CancellationToken cancellationToken);

    /// <summary>
    /// One user's profile and holdings, or <see langword="null"/> when they are not
    /// synchronization-ready. Backs the per-user re-check an operator runs from the user's own
    /// admin page (ADR-115).
    /// </summary>
    /// <remarks>
    /// It applies exactly the readiness conditions <see cref="ListCohortHoldingsAsync"/> does, so
    /// a single-user re-check is the same operation as a cohort repair narrowed to one row rather
    /// than a second, more permissive path into the same convergence pass.
    /// </remarks>
    Task<CohortRepairHolding?> FindUserHoldingAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Flags the given users' connections for the convergence pass, returning how many took the
    /// flag. A user whose connection has since died or whose initial sync never finished is
    /// silently skipped, exactly as <c>TryRequestProfileResync</c> already decides (ADR-096).
    /// </summary>
    /// <param name="removesRetiredLessons">
    /// Whether the operator authorized that pass to also remove lessons no longer published
    /// anywhere (ADR-167). It is carried on the connection, so the permission reaches the worker
    /// with the request it belongs to and is cleared when that request completes.
    /// </param>
    Task<int> RequestConvergenceAsync(
        IReadOnlyCollection<Guid> userIds,
        bool removesRetiredLessons,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
