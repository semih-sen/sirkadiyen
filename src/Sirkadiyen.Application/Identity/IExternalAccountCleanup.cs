namespace Sirkadiyen.Application.Identity;

/// <summary>
/// Removes a deleted account's external Google footprint — its managed calendar and the stored
/// grant — best-effort (ADR-118). Implemented in the infrastructure layer, where the Google client,
/// the token protector and structured logging live, so the use case stays free of both the
/// credentials it decrypts and the provider exceptions it must tolerate.
/// </summary>
/// <remarks>
/// This never throws for an external failure: an account owner has a right to be erased locally
/// whether or not Google is reachable, so a dead token or an unreachable API is reported in the
/// result and logged, never propagated. What it could not do is recorded in the deletion's audit
/// metadata so the outcome is answerable later.
/// </remarks>
public interface IExternalAccountCleanup
{
    Task<ExternalAccountCleanupResult> CleanUpAsync(
        AccountCalendarCleanup credential,
        CancellationToken cancellationToken);
}

/// <summary>What the best-effort external cleanup managed to do (ADR-118).</summary>
public sealed record ExternalAccountCleanupResult
{
    public required bool HadManagedCalendar { get; init; }

    public required bool CalendarDeleted { get; init; }

    public required bool TokenRevoked { get; init; }
}
