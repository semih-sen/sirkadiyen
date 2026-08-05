using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Finds synchronization-ready users by joining Calendar connections to student profiles from
/// PostgreSQL, for incremental fan-out (ADR-059).
/// </summary>
public sealed class CalendarSyncTargetReadStore(SirkadiyenDbContext dbContext)
    : ICalendarSyncTargetReadStore
{
    public async Task<IReadOnlyList<CalendarSyncTarget>> ListCohortTargetsAsync(
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        CancellationToken cancellationToken)
    {
        List<Row> rows = await ReadyTargets()
            .Where(row => row.Profile.AcademicYear == academicYear
                && row.Profile.ClassYear == classYear
                && row.Profile.ProgramLanguage == programLanguage)
            .ToListAsync(cancellationToken);

        return Project(rows);
    }

    public async Task<IReadOnlyList<CalendarSyncTarget>> ListTargetsByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return [];
        }

        List<Row> rows = await ReadyTargets()
            .Where(row => userIds.Contains(row.Connection.UserId))
            .ToListAsync(cancellationToken);

        return Project(rows);
    }

    public async Task<IReadOnlyList<CalendarInventoryTarget>> ListInventoryTargetsAsync(
        DateTimeOffset dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<Row> rows = await ReadyTargets()
            .Where(row =>
                row.Connection.ManagedCalendarUnavailableAtUtc == null
                && row.Connection.ReconciliationRequiredSinceUtc == null
                && (row.Connection.LastCalendarInventoryAtUtc == null
                    || row.Connection.LastCalendarInventoryAtUtc <= dueBeforeUtc))
            .OrderBy(row => row.Connection.LastCalendarInventoryAtUtc)
            .ThenBy(row => row.Connection.UserId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new CalendarInventoryTarget
        {
            UserId = row.Connection.UserId,
            ProtectedRefreshToken = row.Connection.ProtectedRefreshToken,
            ManagedCalendarId = row.Connection.ManagedCalendarId!,
            Profile = ProfileView(row.Profile),
            LastInventoryAtUtc = row.Connection.LastCalendarInventoryAtUtc,
        })];
    }

    /// <summary>
    /// The join of connections that can be written to — authorized, initial sync completed, with a
    /// calendar attached — to their student profile. Both halves are needed: the connection for the
    /// credential and calendar, the profile for the audience decision.
    /// </summary>
    /// <remarks>
    /// An active license is required here as well (ADR-095). Revocation stops future
    /// synchronization, so a revoked student drops out of diff fan-out and periodic inventory on
    /// the next cycle while keeping every event already written to their calendar.
    /// </remarks>
    private IQueryable<Row> ReadyTargets() =>
        from connection in dbContext.GoogleCalendarConnections.AsNoTracking()
        join profile in dbContext.StudentProfiles.AsNoTracking()
            on connection.UserId equals profile.UserId
        where connection.Status == GoogleCalendarConnectionStatus.Authorized
            && connection.InitialSyncState == GoogleCalendarInitialSyncState.Completed
            && connection.ManagedCalendarId != null
            && connection.ManagedCalendarUnavailableAtUtc == null
            && ActiveLicenseQuery.UserIds(dbContext).Contains(connection.UserId)
        select new Row { Connection = connection, Profile = profile };

    private static IReadOnlyList<CalendarSyncTarget> Project(IEnumerable<Row> rows) =>
        [.. rows.Select(row => new CalendarSyncTarget
        {
            UserId = row.Connection.UserId,
            ProtectedRefreshToken = row.Connection.ProtectedRefreshToken,
            ManagedCalendarId = row.Connection.ManagedCalendarId!,
            Profile = ProfileView(row.Profile),
        })];

    private static StudentProfileView ProfileView(StudentProfile profile) => new()
    {
        UserId = profile.UserId,
        AcademicYear = profile.AcademicYear,
        ClassYear = profile.ClassYear,
        ProgramLanguage = profile.ProgramLanguage,
        StudentNumber = profile.StudentNumber,
        SelectorSchemaVersion = profile.SelectorSchemaVersion,
        Selectors = new Dictionary<string, string>(profile.Selectors, StringComparer.Ordinal),
        UpdatedAtUtc = profile.UpdatedAtUtc,
    };

    private sealed class Row
    {
        public required GoogleCalendarConnection Connection { get; init; }

        public required StudentProfile Profile { get; init; }
    }
}
