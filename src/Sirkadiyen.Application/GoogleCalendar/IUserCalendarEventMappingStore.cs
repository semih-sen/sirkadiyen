using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// The durable ledger of which logical lessons have been written to a user's calendar
/// (ADR-058). It is the idempotency record the initial sync consults to know what remains, and the
/// authority incremental sync consults to know who currently holds a lesson (ADR-059).
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
    /// An honest progress projection for one user, derived only from what the ledger actually
    /// records: how many events are on the calendar, how many have been patched since they were
    /// first written, and when the first and last writes happened. The ledger does not record
    /// per-stage "unchanged" or "failed" outcomes, nor the total applicable-record count, so those
    /// are deliberately absent rather than fabricated (AI_GUIDELINE §9).
    /// </summary>
    Task<CalendarSyncProgressView> GetProgressForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Every ledger row for one user, used by the periodic inventory sweep.</summary>
    Task<IReadOnlyList<CalendarEventMappingView>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every user who currently has an event for one logical lesson (ADR-059). This is the reverse of
    /// the initial-sync lookup: given a lesson that changed, it names who must be updated or have the
    /// event removed. Scoped by <paramref name="sourceId"/> so a stable identity is only compared
    /// within the source that produced it.
    /// </summary>
    Task<IReadOnlyList<CalendarEventMappingView>> ListForStableIdentityAsync(
        SourceId sourceId,
        string stableIdentity,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a written event. A concurrent or repeated write of the same
    /// <c>(UserId, StableIdentity)</c> is reported as
    /// <see cref="CalendarEventMappingAddOutcome.AlreadyPresent"/> rather than failing.
    /// </summary>
    Task<CalendarEventMappingAddOutcome> AddAsync(
        UserCalendarEventMapping mapping,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records that one user's event now reflects a newer canonical record and content (ADR-059),
    /// after the calendar has been patched. A mapping that no longer exists is a safe no-op.
    /// </summary>
    Task UpdateContentAsync(
        Guid userId,
        string stableIdentity,
        Guid canonicalRecordId,
        string contentHash,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically moves a mapping from the previous to the current stable identity after a
    /// secondary semantic match, preserving its Google event id (ADR-035, ADR-060).
    /// </summary>
    Task<CalendarEventMappingReidentifyOutcome> ReidentifyAsync(
        Guid userId,
        SourceId sourceId,
        string previousStableIdentity,
        string currentStableIdentity,
        Guid canonicalRecordId,
        string contentHash,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes one user's ledger row for a lesson after its event has been deleted (ADR-059). A row
    /// that is already gone is reported as <see cref="CalendarEventMappingRemoveOutcome.NotFound"/>.
    /// </summary>
    Task<CalendarEventMappingRemoveOutcome> RemoveAsync(
        Guid userId,
        string stableIdentity,
        CancellationToken cancellationToken);
}

/// <summary>
/// A ledger-derived progress read for one user's initial/incremental synchronization.
/// </summary>
/// <remarks>
/// Every mapped row is an event currently on the calendar. A row whose content was patched after
/// it was first written has <c>UpdatedAtUtc &gt; CreatedAtUtc</c>; the remainder were written once
/// and never changed. These are the only counts the ledger can state truthfully.
/// </remarks>
public sealed record CalendarSyncProgressView
{
    /// <summary>Total events currently mapped onto the user's calendar.</summary>
    public required int MappedEventCount { get; init; }

    /// <summary>Of the mapped events, how many have been patched since first written.</summary>
    public required int UpdatedEventCount { get; init; }

    /// <summary>When the first event was written, or <see langword="null"/> when none have been.</summary>
    public DateTimeOffset? FirstWrittenAtUtc { get; init; }

    /// <summary>When the most recent write happened, or <see langword="null"/> when none have been.</summary>
    public DateTimeOffset? LastWrittenAtUtc { get; init; }
}

/// <summary>A read projection of one ledger row, for the incremental reverse lookup.</summary>
public sealed record CalendarEventMappingView
{
    public required Guid UserId { get; init; }

    public required string StableIdentity { get; init; }

    public required string GoogleCalendarId { get; init; }

    public required string GoogleEventId { get; init; }

    /// <summary>The content hash last written, so a patch can be skipped when nothing changed.</summary>
    public required string ContentHash { get; init; }

    public required Guid CanonicalRecordId { get; init; }
}

public enum CalendarEventMappingAddOutcome
{
    Added,

    /// <summary>A mapping for this user and lesson already existed.</summary>
    AlreadyPresent,
}

public enum CalendarEventMappingRemoveOutcome
{
    Removed,

    /// <summary>No mapping for this user and lesson existed to remove.</summary>
    NotFound,
}

public enum CalendarEventMappingReidentifyOutcome
{
    Reidentified,

    /// <summary>A retried pass found the mapping already at the current identity.</summary>
    AlreadyReidentified,

    /// <summary>Neither the previous nor current identity has a mapping for this user.</summary>
    NotFound,

    /// <summary>Both identities exist, or one belongs to another source; automatic merge is unsafe.</summary>
    Conflict,
}
