using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence.StudentProfiles.Stores;

/// <summary>
/// Reads one program's stored profiles for the roster-profile audit in PostgreSQL (ADR-159).
/// </summary>
/// <remarks>
/// A single read. The audit corrects through the student's own write path, so this store never
/// writes and needs no transaction of its own: it hands the planner the cohort's profiles and the
/// live lists do the rest.
/// </remarks>
public sealed class RosterProfileAuditStore(SirkadiyenDbContext dbContext)
    : IRosterProfileAuditStore
{
    public async Task<IReadOnlyList<StudentProfileView>> ListCohortProfilesAsync(
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);

        List<StudentProfile> profiles = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.AcademicYear == academicYear
                && profile.ClassYear == classYear
                && profile.ProgramLanguage == programLanguage)
            .OrderBy(profile => profile.UserId)
            .ToListAsync(cancellationToken);

        return
        [
            .. profiles.Select(profile => new StudentProfileView
            {
                UserId = profile.UserId,
                AcademicYear = profile.AcademicYear,
                ClassYear = profile.ClassYear,
                ProgramLanguage = profile.ProgramLanguage,
                StudentNumber = profile.StudentNumber,
                SelectorSchemaVersion = profile.SelectorSchemaVersion,
                Selectors = new Dictionary<string, string>(profile.Selectors, StringComparer.Ordinal),
                UpdatedAtUtc = profile.UpdatedAtUtc,
            }),
        ];
    }
}
