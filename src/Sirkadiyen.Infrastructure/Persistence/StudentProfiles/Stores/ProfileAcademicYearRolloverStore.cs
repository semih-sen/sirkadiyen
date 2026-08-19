using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Stores;

/// <summary>
/// Reads and applies a program's academic-year rollover in PostgreSQL (ADR-115).
/// </summary>
/// <remarks>
/// The candidate query joins profiles to their ledger rows in two round trips rather than one per
/// student, for the reason the cohort repair store already does: a program is a few hundred
/// students holding around a thousand events each, and a query per student would make planning
/// the rollover slower than performing it.
/// </remarks>
public sealed class ProfileAcademicYearRolloverStore(SirkadiyenDbContext dbContext)
    : IProfileAcademicYearRolloverStore
{
    public async Task<IReadOnlyList<ProfileRolloverCandidate>> ListCandidatesAsync(
        ProfileRolloverScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // Deliberately not filtered by connection state. The year on a profile decides what its
        // owner receives whenever they connect, so leaving an unconnected student on the old year
        // would only defer the same empty calendar to their first sync.
        List<StudentProfile> profiles = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.AcademicYear == scope.FromAcademicYear
                && profile.ClassYear == scope.ClassYear
                && profile.ProgramLanguage == scope.ProgramLanguage)
            .OrderBy(profile => profile.UserId)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return [];
        }

        List<Guid> userIds = [.. profiles.Select(profile => profile.UserId)];

        // "Sync ready" is the same set the convergence pass will actually accept a flag for
        // (ADR-096), so the plan's queueable count is what the apply will really queue rather
        // than an optimistic total.
        HashSet<Guid> syncReady = [.. await dbContext.GoogleCalendarConnections
            .AsNoTracking()
            .Where(connection => userIds.Contains(connection.UserId)
                && connection.Status == GoogleCalendarConnectionStatus.Authorized
                && connection.InitialSyncState == GoogleCalendarInitialSyncState.Completed
                && connection.ManagedCalendarId != null
                && connection.ManagedCalendarUnavailableAtUtc == null)
            .Select(connection => connection.UserId)
            .ToListAsync(cancellationToken)];

        List<UserCalendarEventMapping> mappings = await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .Where(mapping => userIds.Contains(mapping.UserId))
            .ToListAsync(cancellationToken);

        ILookup<Guid, UserCalendarEventMapping> byUser =
            mappings.ToLookup(mapping => mapping.UserId);

        return
        [
            .. profiles.Select(profile => new ProfileRolloverCandidate
            {
                UserId = profile.UserId,
                Profile = ProfileView(profile),
                HasSyncReadyConnection = syncReady.Contains(profile.UserId),
                Held =
                [
                    .. byUser[profile.UserId]
                        .OrderBy(mapping => mapping.StableIdentity, StringComparer.Ordinal)
                        .Select(mapping => new HeldLessonIdentity
                        {
                            SourceId = mapping.SourceId.Value,
                            StableIdentity = mapping.StableIdentity,
                        }),
                ],
            }),
        ];
    }

    public async Task<IReadOnlyList<DriftedProfile>> ListDriftedAsync(
        int classYear,
        ProgramLanguage programLanguage,
        string expectedAcademicYear,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAcademicYear);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<StudentProfile> profiles = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.ClassYear == classYear
                && profile.ProgramLanguage == programLanguage
                && profile.AcademicYear != expectedAcademicYear)
            // Oldest first, so the profiles that have been stranded longest are repaired first
            // rather than a bounded batch repeatedly picking the same arbitrary slice.
            .OrderBy(profile => profile.UpdatedAtUtc)
            .ThenBy(profile => profile.UserId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return
        [
            .. profiles.Select(profile => new DriftedProfile
            {
                UserId = profile.UserId,
                Profile = ProfileView(profile),
            }),
        ];
    }

    public async Task<ProfileRolloverApplyResult> ApplyAsync(
        IReadOnlyCollection<Guid> userIds,
        string toAcademicYear,
        string toSchemaVersion,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(toAcademicYear);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSchemaVersion);

        if (userIds.Count == 0)
        {
            return new ProfileRolloverApplyResult
            {
                ProfilesMoved = 0,
                ConvergenceRequested = 0,
            };
        }

        return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            List<StudentProfile> profiles = await dbContext.StudentProfiles
                .Where(profile => userIds.Contains(profile.UserId))
                .ToListAsync(cancellationToken);

            // Only the year and the schema version. A rollover must never be able to touch a
            // selector or a student number, which is why this does not go through the upsert the
            // student's own save uses. A profile already on the target year is not counted as
            // moved, so a re-run of an interrupted rollover reports what it actually changed.
            int moved = profiles.Count(profile =>
                profile.MoveToAcademicYear(toAcademicYear, toSchemaVersion, atUtc));

            List<GoogleCalendarConnection> connections = await dbContext.GoogleCalendarConnections
                .Where(connection => userIds.Contains(connection.UserId))
                .ToListAsync(cancellationToken);

            // The domain decides whether a connection can take the flag: one whose initial sync
            // never finished absorbs the audience when it runs, and one with no calendar has
            // nothing to converge (ADR-096). An existing request keeps its original timestamp, so
            // a rollover never pushes an older unconverged profile change to the back of the queue.
            int requested = connections.Count(connection => connection.TryRequestProfileResync(atUtc));

            // One transaction for both writes, for the reason the profile upsert already shares
            // one: a profile moved to a new year while nothing knows the calendar must follow is
            // precisely the state this operation exists to repair.
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ProfileRolloverApplyResult
            {
                ProfilesMoved = moved,
                ConvergenceRequested = requested,
            };
        });
    }

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
}
