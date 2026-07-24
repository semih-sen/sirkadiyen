using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// The durable ledger of which logical lessons have been written to a user's calendar
/// (ADR-058). It is the idempotency record the initial sync consults to know what remains.
/// </summary>
public interface IUserCalendarEventMappingStore
{
    /// <summary>The stable identities already written for the user, for computing the remainder.</summary>
    Task<IReadOnlySet<string>> ListStableIdentitiesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>How many events have been mapped for the user, for a progress read.</summary>
    Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a written event. A concurrent or repeated write of the same
    /// <c>(UserId, StableIdentity)</c> is reported as
    /// <see cref="CalendarEventMappingAddOutcome.AlreadyPresent"/> rather than failing.
    /// </summary>
    Task<CalendarEventMappingAddOutcome> AddAsync(
        UserCalendarEventMapping mapping,
        CancellationToken cancellationToken);
}

public enum CalendarEventMappingAddOutcome
{
    Added,

    /// <summary>A mapping for this user and lesson already existed.</summary>
    AlreadyPresent,
}
