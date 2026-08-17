using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Records what a Calendar write just discovered about a connection's health: a rejected
/// credential, or a managed calendar that is no longer there.
/// </summary>
/// <remarks>
/// Split out of <see cref="ICalendarSyncConnectionStore"/> so a service that only writes to
/// calendars does not depend on the whole worker-side connection lifecycle (ADR-107). Announcement
/// delivery is such a service: it never lists pending syncs, advances a replay cursor or completes
/// an inventory, but it is a real Calendar write and therefore discovers these two facts.
/// <para>
/// Recording them here rather than letting the schedule path rediscover them matters: a student
/// whose grant died would otherwise keep an <c>Authorized</c> connection until the next published
/// revision happened to write to them, which can be days.
/// </para>
/// </remarks>
public interface ICalendarConnectionHealthWriter
{
    /// <summary>
    /// Records that Google rejected a user's credential, moving the connection to
    /// <see cref="GoogleCalendarConnectionStatus.NeedsReauthorization"/> (ADR-059). Idempotent.
    /// </summary>
    Task MarkNeedsReauthorizationAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the attached calendar unavailable so ordinary synchronization stops and the user sees
    /// an explicit repair-required state.
    /// </summary>
    Task MarkManagedCalendarUnavailableAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
