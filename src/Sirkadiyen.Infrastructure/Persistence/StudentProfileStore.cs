using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence;

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

    public async Task<StudentProfileView> UpsertAsync(
        Guid userId,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        string selectorSchemaVersion,
        IReadOnlyDictionary<string, string> selectors,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        await UpsertAsync(
            userId,
            academicYear,
            classYear,
            programLanguage,
            selectorSchemaVersion,
            selectors,
            atUtc,
            retryAfterConcurrentInsert: true,
            cancellationToken);

    private async Task<StudentProfileView> UpsertAsync(
        Guid userId,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
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

                if (profile is null)
                {
                    profile = StudentProfile.Create(
                        userId,
                        academicYear,
                        classYear,
                        programLanguage,
                        selectorSchemaVersion,
                        selectors,
                        atUtc);
                    dbContext.StudentProfiles.Add(profile);
                }
                else
                {
                    profile.Update(
                        academicYear,
                        classYear,
                        programLanguage,
                        selectorSchemaVersion,
                        selectors,
                        atUtc);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return View(profile);
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
                selectorSchemaVersion,
                selectors,
                atUtc,
                retryAfterConcurrentInsert: false,
                cancellationToken);
        }
    }

    private static StudentProfileView View(StudentProfile profile) => new()
    {
        UserId = profile.UserId,
        AcademicYear = profile.AcademicYear,
        ClassYear = profile.ClassYear,
        ProgramLanguage = profile.ProgramLanguage,
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
