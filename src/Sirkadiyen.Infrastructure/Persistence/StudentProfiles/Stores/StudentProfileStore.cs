using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Stores;

/// <summary>Transactional PostgreSQL student-profile store.</summary>
public sealed class StudentProfileStore(SirkadiyenDbContext dbContext) : IStudentProfileStore
{
    public async Task<StudentProfileView?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        StudentProfile? profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        return profile is null ? null : View(profile);
    }

    public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.StudentProfiles
            .AsNoTracking()
            .AnyAsync(candidate => candidate.UserId == userId, cancellationToken);

    public async Task<StudentProfileUpsertResult> UpsertAsync(
        Guid userId,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        string studentNumber,
        string selectorSchemaVersion,
        IReadOnlyDictionary<string, string> selectors,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        await UpsertAsync(
            userId,
            academicYear,
            classYear,
            programLanguage,
            studentNumber,
            selectorSchemaVersion,
            selectors,
            atUtc,
            retryAfterConcurrentInsert: true,
            cancellationToken);

    private async Task<StudentProfileUpsertResult> UpsertAsync(
        Guid userId,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        string studentNumber,
        string selectorSchemaVersion,
        IReadOnlyDictionary<string, string> selectors,
        DateTimeOffset atUtc,
        bool retryAfterConcurrentInsert,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RetriableTransaction.ExecuteAsync(dbContext, async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                StudentProfile? profile = await dbContext.StudentProfiles
                    .SingleOrDefaultAsync(
                        candidate => candidate.UserId == userId,
                        cancellationToken);

                bool audienceChanged = false;

                if (profile is null)
                {
                    profile = StudentProfile.Create(
                        userId,
                        academicYear,
                        classYear,
                        programLanguage,
                        studentNumber,
                        selectorSchemaVersion,
                        selectors,
                        atUtc);
                    dbContext.StudentProfiles.Add(profile);
                }
                else
                {
                    // Asked before the update, while the stored values are still the old ones.
                    // A first profile is never an audience change: there is nothing on a calendar
                    // yet, and initial sync resolves the audience when it runs (ADR-096).
                    audienceChanged = !profile.DescribesSameAudienceAs(
                        academicYear,
                        classYear,
                        programLanguage,
                        selectors);

                    profile.Update(
                        academicYear,
                        classYear,
                        programLanguage,
                        studentNumber,
                        selectorSchemaVersion,
                        selectors,
                        atUtc);
                }

                bool resyncRequested = audienceChanged
                    && await RequestCalendarResyncAsync(userId, atUtc, cancellationToken);

                // One transaction for both writes. A profile that has moved to a new cohort while
                // nothing knows the calendar must follow is precisely the state ADR-096 exists to
                // prevent, so the request cannot be lost to a crash between them.
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new StudentProfileUpsertResult
                {
                    Profile = View(profile),
                    AudienceChanged = audienceChanged,
                    CalendarResyncRequested = resyncRequested,
                };
            });
        }
        catch (DbUpdateException exception)
            when (retryAfterConcurrentInsert && IsUniqueViolation(exception))
        {
            // Two concurrent first-time saves for one user can race the unique
            // index. The loser reruns exactly once, now finding the winning row
            // and applying its own values as an update.
            dbContext.ChangeTracker.Clear();
            return await UpsertAsync(
                userId,
                academicYear,
                classYear,
                programLanguage,
                studentNumber,
                selectorSchemaVersion,
                selectors,
                atUtc,
                retryAfterConcurrentInsert: false,
                cancellationToken);
        }
    }

    /// <summary>
    /// Records that the user's calendar must be converged onto the new audience (ADR-096), inside
    /// the caller's transaction. Returns whether a connection was there to flag.
    /// </summary>
    private async Task<bool> RequestCalendarResyncAsync(
        Guid userId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        GoogleCalendarConnection? connection = await dbContext.GoogleCalendarConnections
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        // No connection, or one whose initial sync has not finished, needs nothing: initial sync
        // reads the profile as it stands when it runs.
        return connection is not null && connection.TryRequestProfileResync(atUtc);
    }

    private static StudentProfileView View(StudentProfile profile) => new()
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

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };
}
