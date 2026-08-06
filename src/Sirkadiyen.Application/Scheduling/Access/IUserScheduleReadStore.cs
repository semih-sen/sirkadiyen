using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.Scheduling.Access;

/// <summary>
/// Reads a student's own timetable straight from what has actually been written to their calendar
/// — the event-mapping ledger joined to the canonical records it points at. Reading the ledger,
/// rather than re-resolving the published schedule, guarantees the student sees exactly what is on
/// their calendar (ADR-058, ADR-059).
/// </summary>
public interface IUserScheduleReadStore
{
    /// <summary>
    /// The user's managed events falling on or between the two local dates, earliest first.
    /// </summary>
    Task<IReadOnlyList<UserScheduleEventView>> ListUpcomingAsync(
        Guid userId,
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// The user's most recently written or patched managed events, newest change first.
    /// </summary>
    /// <remarks>
    /// This is derived from the ledger, which holds only events currently on the calendar, so it
    /// reports creations and updates but not deletions: a removed event leaves no ledger row to
    /// report (AI_GUIDELINE §9). A faithful deletion feed would need a separate per-user activity
    /// log written by the sync services.
    /// </remarks>
    Task<IReadOnlyList<UserScheduleChangeView>> ListRecentChangesAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>One lesson on the user's calendar, projected for display.</summary>
public sealed record UserScheduleEventView
{
    public required string StableIdentity { get; init; }

    public required string Title { get; init; }

    public required DateOnly LocalDate { get; init; }

    public TimeOnly? StartLocalTime { get; init; }

    public TimeOnly? EndLocalTime { get; init; }

    public required bool IsAllDay { get; init; }

    public required string TimeZoneId { get; init; }

    public string? Location { get; init; }

    public string? Instructor { get; init; }

    public required ScheduleEventType EventType { get; init; }

    public required IReadOnlyList<string> Departments { get; init; }
}

/// <summary>A recent creation or update applied to the user's calendar.</summary>
public sealed record UserScheduleChangeView
{
    public required string StableIdentity { get; init; }

    public required string Title { get; init; }

    public required DateOnly LocalDate { get; init; }

    public required UserScheduleChangeKind Kind { get; init; }

    public required DateTimeOffset ChangedAtUtc { get; init; }
}

public enum UserScheduleChangeKind
{
    /// <summary>The event was written once and has not been patched since.</summary>
    Created,

    /// <summary>The event has been patched at least once since it was first written.</summary>
    Updated,
}
