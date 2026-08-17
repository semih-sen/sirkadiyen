using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.StudentProfiles;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;

namespace Sirkadiyen.Infrastructure.Persistence.Announcements.Stores;

/// <summary>
/// Resolves an announcement audience from PostgreSQL, reporting the ineligible candidates and why
/// (ADR-107).
/// </summary>
public sealed class AnnouncementAudienceReadStore(SirkadiyenDbContext dbContext)
    : IAnnouncementAudienceReadStore
{
    public async Task<AnnouncementAudienceResolution> ResolveAsync(
        AnnouncementAudienceCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        List<Row> rows = criteria.TargetUserId is { } targetUserId
            ? await SingleUserAsync(targetUserId, cancellationToken)
            : await CohortAsync(criteria, cancellationToken);

        List<AnnouncementAudienceCandidate> included = [];
        List<AnnouncementAudienceCandidate> excluded = [];

        foreach (Row row in rows.OrderBy(row => row.User.Id))
        {
            // The cohort query cannot express a JSONB selector match, so the profile filter is
            // applied here through the one pure rule both sides share. The set it filters is a
            // single academic year's students, which is small enough to read.
            if (criteria.TargetUserId is null
                && (row.Profile is null
                    || !AnnouncementAudienceMatcher.Matches(
                        criteria.Selectors,
                        row.Profile.Selectors)))
            {
                continue;
            }

            AnnouncementExclusionReason? reason = Ineligibility(row);
            AnnouncementAudienceCandidate candidate = new()
            {
                UserId = row.User.Id,
                Email = row.User.Email,
                DisplayName = row.User.DisplayName,
                ClassYear = row.Profile?.ClassYear,
                ProgramLanguage = row.Profile?.ProgramLanguage,
                ManagedCalendarId = reason is null ? row.Connection!.ManagedCalendarId : null,
                ExclusionReason = reason,
            };

            if (reason is null)
            {
                included.Add(candidate);
            }
            else
            {
                excluded.Add(candidate);
            }
        }

        return new AnnouncementAudienceResolution { Included = included, Excluded = excluded };
    }

    /// <summary>
    /// Why this candidate cannot receive a copy, or null when they can. The order matters: it is
    /// the order the operator would fix them in, and the first blocking fact is the one reported.
    /// </summary>
    private static AnnouncementExclusionReason? Ineligibility(Row row)
    {
        if (row.Profile is null)
        {
            return AnnouncementExclusionReason.NoStudentProfile;
        }

        if (!row.HasActiveLicense)
        {
            // Revocation stops future calendar writes (ADR-095), so this is not a filter the
            // operator could waive — there would be nothing to write to.
            return AnnouncementExclusionReason.LicenseInactive;
        }

        if (row.Connection is null)
        {
            return AnnouncementExclusionReason.NoCalendarConnection;
        }

        if (row.Connection.Status is not GoogleCalendarConnectionStatus.Authorized)
        {
            return AnnouncementExclusionReason.CalendarAuthorizationRevoked;
        }

        if (row.Connection.ManagedCalendarUnavailableAtUtc is not null)
        {
            return AnnouncementExclusionReason.ManagedCalendarUnavailable;
        }

        if (row.Connection.InitialSyncState is not GoogleCalendarInitialSyncState.Completed
            || string.IsNullOrWhiteSpace(row.Connection.ManagedCalendarId))
        {
            return AnnouncementExclusionReason.InitialSyncIncomplete;
        }

        return null;
    }

    private Task<List<Row>> SingleUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Candidates().Where(row => row.User.Id == userId).ToListAsync(cancellationToken);

    private Task<List<Row>> CohortAsync(
        AnnouncementAudienceCriteria criteria,
        CancellationToken cancellationToken)
    {
        IQueryable<Row> query = Candidates()
            .Where(row => row.Profile != null
                && row.Profile.AcademicYear == criteria.AcademicYear);

        if (criteria.ClassYear is { } classYear)
        {
            query = query.Where(row => row.Profile!.ClassYear == classYear);
        }

        if (criteria.ProgramLanguage is { } language)
        {
            query = query.Where(row => row.Profile!.ProgramLanguage == language);
        }

        return query.ToListAsync(cancellationToken);
    }

    /// <remarks>
    /// Left joins throughout, because the ineligible candidates are the point: an account with no
    /// profile, no license or no Calendar grant has to reach the operator's exclusion list rather
    /// than vanish from the query that produced the recipient count.
    /// </remarks>
    private IQueryable<Row> Candidates() =>
        from user in dbContext.Users.AsNoTracking()
        join profile in dbContext.StudentProfiles.AsNoTracking()
            on user.Id equals profile.UserId into profiles
        from profile in profiles.DefaultIfEmpty()
        join connection in dbContext.GoogleCalendarConnections.AsNoTracking()
            on user.Id equals connection.UserId into connections
        from connection in connections.DefaultIfEmpty()
        select new Row
        {
            User = user,
            Profile = profile,
            Connection = connection,
            HasActiveLicense = ActiveLicenseQuery.UserIds(dbContext).Contains(user.Id),
        };

    private sealed class Row
    {
        public required User User { get; init; }

        public StudentProfile? Profile { get; init; }

        public GoogleCalendarConnection? Connection { get; init; }

        public required bool HasActiveLicense { get; init; }
    }
}
