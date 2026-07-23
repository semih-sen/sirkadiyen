using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>Persistence boundary for the single Calendar connection a user owns.</summary>
public interface IGoogleCalendarConnectionStore
{
    Task<GoogleCalendarConnectionView?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the user has a connection that is currently usable. A row that needs
    /// re-authorization does not count, so onboarding sends the user back to consent
    /// rather than reporting a connection that cannot synchronize.
    /// </summary>
    Task<bool> IsAuthorizedForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Records an authorization, replacing any existing one, transactionally.</summary>
    Task<GoogleCalendarConnectionView> UpsertAuthorizationAsync(
        Guid userId,
        string protectedRefreshToken,
        string grantedScopes,
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

    /// <summary>Null until initial sync creates the dedicated calendar (ADR-024).</summary>
    public string? ManagedCalendarId { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
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
