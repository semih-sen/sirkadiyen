using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Domain.StudentProfiles;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;

namespace Sirkadiyen.Infrastructure.Persistence.GoogleCalendar.Stores;

/// <summary>
/// Reads what a cohort's calendars hold and flags them for convergence (ADR-111).
/// </summary>
/// <remarks>
/// The holdings query joins connections to profiles to ledger rows in one round trip rather than
/// per user: a Grade 3 program is a few hundred students holding around a thousand events each,
/// and a query per student would make planning a repair slower than performing it.
/// </remarks>
public sealed class CohortCalendarRepairStore(SirkadiyenDbContext dbContext)
    : ICohortCalendarRepairStore
{
    public async Task<IReadOnlyList<CohortRepairHolding>> ListCohortHoldingsAsync(
        CohortRepairScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        List<ProfileRow> profiles = await
            (from connection in dbContext.GoogleCalendarConnections.AsNoTracking()
             join profile in dbContext.StudentProfiles.AsNoTracking()
                 on connection.UserId equals profile.UserId
             where connection.Status == GoogleCalendarConnectionStatus.Authorized
                 && connection.InitialSyncState == GoogleCalendarInitialSyncState.Completed
                 && connection.ManagedCalendarId != null
                 && connection.ManagedCalendarUnavailableAtUtc == null
                 && ActiveLicenseQuery.UserIds(dbContext).Contains(connection.UserId)
                 && profile.AcademicYear == scope.AcademicYear
                 && profile.ClassYear == scope.ClassYear
                 && profile.ProgramLanguage == scope.ProgramLanguage
             orderby connection.UserId
             select new ProfileRow { UserId = connection.UserId, Profile = profile })
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return [];
        }

        List<Guid> userIds = [.. profiles.Select(row => row.UserId)];

        List<UserCalendarEventMapping> mappings = await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .Where(mapping => userIds.Contains(mapping.UserId))
            .ToListAsync(cancellationToken);

        ILookup<Guid, UserCalendarEventMapping> byUser =
            mappings.ToLookup(mapping => mapping.UserId);

        return
        [
            .. profiles.Select(row => new CohortRepairHolding
            {
                UserId = row.UserId,
                Profile = ProfileView(row.Profile),
                Mappings =
                [
                    .. byUser[row.UserId]
                        .OrderBy(mapping => mapping.StableIdentity, StringComparer.Ordinal)
                        .Select(View),
                ],
            }),
        ];
    }

    public async Task<CohortRepairHolding?> FindUserHoldingAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        StudentProfile? profile = await
            (from connection in dbContext.GoogleCalendarConnections.AsNoTracking()
             join candidate in dbContext.StudentProfiles.AsNoTracking()
                 on connection.UserId equals candidate.UserId
             where connection.UserId == userId
                 && connection.Status == GoogleCalendarConnectionStatus.Authorized
                 && connection.InitialSyncState == GoogleCalendarInitialSyncState.Completed
                 && connection.ManagedCalendarId != null
                 && connection.ManagedCalendarUnavailableAtUtc == null
                 && ActiveLicenseQuery.UserIds(dbContext).Contains(connection.UserId)
             select candidate)
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        List<UserCalendarEventMapping> mappings = await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .Where(mapping => mapping.UserId == userId)
            .OrderBy(mapping => mapping.StableIdentity)
            .ToListAsync(cancellationToken);

        return new CohortRepairHolding
        {
            UserId = userId,
            Profile = ProfileView(profile),
            Mappings = [.. mappings.Select(View)],
        };
    }

    public async Task<int> RequestConvergenceAsync(
        IReadOnlyCollection<Guid> userIds,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return 0;
        }

        List<GoogleCalendarConnection> connections = await dbContext.GoogleCalendarConnections
            .Where(connection => userIds.Contains(connection.UserId))
            .ToListAsync(cancellationToken);

        // The domain decides whether a connection can take the flag: one whose initial sync never
        // finished absorbs the audience when it runs, and one with no calendar has nothing to
        // converge (ADR-096). An existing request keeps its original timestamp, so a repair never
        // pushes an older unconverged profile change to the back of the queue.
        int requested = connections.Count(connection => connection.TryRequestProfileResync(atUtc));

        await dbContext.SaveChangesAsync(cancellationToken);
        return requested;
    }

    private static CalendarEventMappingView View(UserCalendarEventMapping mapping) => new()
    {
        UserId = mapping.UserId,
        StableIdentity = mapping.StableIdentity,
        SourceId = mapping.SourceId,
        GoogleCalendarId = mapping.GoogleCalendarId,
        GoogleEventId = mapping.GoogleEventId,
        ContentHash = mapping.ContentHash,
        CanonicalRecordId = mapping.CanonicalRecordId,
    };

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

    private sealed class ProfileRow
    {
        public required Guid UserId { get; init; }

        public required StudentProfile Profile { get; init; }
    }
}
