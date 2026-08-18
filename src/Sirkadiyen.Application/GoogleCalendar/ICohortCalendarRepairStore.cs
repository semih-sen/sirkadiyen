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
    /// Flags the given users' connections for the convergence pass, returning how many took the
    /// flag. A user whose connection has since died or whose initial sync never finished is
    /// silently skipped, exactly as <c>TryRequestProfileResync</c> already decides (ADR-096).
    /// </summary>
    Task<int> RequestConvergenceAsync(
        IReadOnlyCollection<Guid> userIds,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
