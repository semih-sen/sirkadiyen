using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Finds the users whose dedicated calendar a newly created or widened lesson may need to reach
/// (ADR-059): those who have finished their initial sync and belong to the lesson's program cohort.
/// </summary>
/// <remarks>
/// This is the forward, audience-driven half of incremental fan-out. The reverse half — who already
/// holds a lesson that changed — comes from the mapping ledger
/// (<see cref="IUserCalendarEventMappingStore.ListForStableIdentityAsync"/>). Cohort filtering is
/// done here in the query; the finer audience-selector test is a separate, pure step so it stays
/// unit-testable.
/// </remarks>
public interface ICalendarSyncTargetReadStore
{
    /// <summary>
    /// Every synchronization-ready user in a program cohort (ADR-059): used to fan a created or
    /// widened lesson out to everyone it might now apply to.
    /// </summary>
    Task<IReadOnlyList<CalendarSyncTarget>> ListCohortTargetsAsync(
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        CancellationToken cancellationToken);

    /// <summary>
    /// The synchronization-ready targets among a specific set of users (ADR-059): used to reach the
    /// current holders of a lesson that changed, drawn from the mapping ledger. A user who is not
    /// authorized or has not finished initial sync is simply absent — which is how a dead-credential
    /// holder is skipped without deleting their events.
    /// </summary>
    Task<IReadOnlyList<CalendarSyncTarget>> ListTargetsByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists healthy, caught-up connections whose periodic Calendar/ledger inventory is due.
    /// Re-authorization replay always finishes before a level-triggered inventory pass begins.
    /// </summary>
    Task<IReadOnlyList<CalendarInventoryTarget>> ListInventoryTargetsAsync(
        DateTimeOffset dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// A user eligible to receive incremental calendar writes: an authorized, initial-sync-completed
/// connection joined to the student profile that decides audience.
/// </summary>
public sealed record CalendarSyncTarget
{
    public required Guid UserId { get; init; }

    /// <summary>The user's encrypted refresh token, decrypted only in memory during a write.</summary>
    public required string ProtectedRefreshToken { get; init; }

    /// <summary>The dedicated calendar the user's events live in; never null for a completed sync.</summary>
    public required string ManagedCalendarId { get; init; }

    public required StudentProfileView Profile { get; init; }
}

public sealed record CalendarInventoryTarget
{
    public required Guid UserId { get; init; }

    public required string ProtectedRefreshToken { get; init; }

    public required string ManagedCalendarId { get; init; }

    public required StudentProfileView Profile { get; init; }

    public DateTimeOffset? LastInventoryAtUtc { get; init; }
}
