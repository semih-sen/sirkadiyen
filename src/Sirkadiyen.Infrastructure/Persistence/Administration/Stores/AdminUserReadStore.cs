using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence.Administration.Stores;

/// <summary>
/// Composes the admin user list and detail from identity, profile, license and Calendar-connection
/// tables.
/// </summary>
/// <remarks>
/// The license state is derived here rather than stored, so it can never disagree with the license
/// table it is read from. Everything else is a projection of a row that already exists; nothing in
/// this store writes.
/// </remarks>
public sealed class AdminUserReadStore(SirkadiyenDbContext dbContext) : IAdminUserReadStore
{
    private const int MaximumPageSize = 200;

    public async Task<PagedResult<AdminUserListItem>> ListAsync(
        AdminUserQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);

        IQueryable<User> users = dbContext.Users.AsNoTracking();

        users = ApplyIdentityFilters(users, query);
        users = ApplyProfileFilters(users, query);
        users = ApplyCalendarFilters(users, query);
        users = ApplyLicenseFilter(users, query.LicenseState);

        if (await ApplySelectorFilterAsync(query, cancellationToken) is { } selectorMatches)
        {
            // EF cannot translate a lookup into the JSONB selector dictionary, so the matching
            // accounts are resolved first and the directory query is narrowed to them. This is the
            // same trade the announcement audience query makes (ADR-107) and it is sound at a
            // medical faculty's scale, but it is a scan that grows with the student body.
            users = users.Where(user => selectorMatches.Contains(user.Id));
        }

        int totalCount = await users.CountAsync(cancellationToken);

        List<AdminUserListItem> items = await Project(
                Sort(users, query).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        Dictionary<Guid, int> eventCounts = await ManagedEventCountsAsync(
            [.. items.Select(item => item.Id)],
            cancellationToken);

        return new PagedResult<AdminUserListItem>
        {
            Items =
            [
                .. items.Select(item => item with
                {
                    ManagedEventCount = eventCounts.GetValueOrDefault(item.Id),
                }),
            ],
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<AdminUserDetail?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        AdminUserListItem? summary = await Project(
                dbContext.Users.AsNoTracking().Where(user => user.Id == userId))
            .SingleOrDefaultAsync(cancellationToken);

        if (summary is null)
        {
            return null;
        }

        // The profile is materialized and then mapped, because its Selectors dictionary is stored
        // as JSON and is simplest to read after materialization.
        StudentProfile? profileEntity = await dbContext.StudentProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        AdminUserProfile? profile = profileEntity is null
            ? null
            : new AdminUserProfile
            {
                AcademicYear = profileEntity.AcademicYear,
                ClassYear = profileEntity.ClassYear,
                ProgramLanguage = profileEntity.ProgramLanguage,
                StudentNumber = profileEntity.StudentNumber,
                SelectorSchemaVersion = profileEntity.SelectorSchemaVersion,
                Selectors = profileEntity.Selectors,
                UpdatedAtUtc = profileEntity.UpdatedAtUtc,
            };

        int managedEventCount = await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .CountAsync(mapping => mapping.UserId == userId, cancellationToken);

        AdminUserCalendarConnection? connection = await dbContext.GoogleCalendarConnections
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => new AdminUserCalendarConnection
            {
                Status = candidate.Status,
                InitialSyncState = candidate.InitialSyncState,
                HasManagedCalendar = candidate.ManagedCalendarId != null,
                ManagedCalendarUnavailableAtUtc = candidate.ManagedCalendarUnavailableAtUtc,
                LastCalendarInventoryAtUtc = candidate.LastCalendarInventoryAtUtc,
                ProfileResyncRequiredSinceUtc = candidate.ProfileResyncRequiredSinceUtc,
                ReconciliationRequiredSinceUtc = candidate.ReconciliationRequiredSinceUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);

        List<AdminUserLicense> licenses = await dbContext.Licenses
            .AsNoTracking()
            .Where(license => license.RedeemedByUserId == userId)
            .OrderByDescending(license => license.CreatedAtUtc)
            .Select(license => new AdminUserLicense
            {
                LicenseId = license.Id,
                Kind = license.Kind,
                Status = license.Status,
                CreatedAtUtc = license.CreatedAtUtc,
                RedeemedAtUtc = license.RedeemedAtUtc,
                RevokedAtUtc = license.RevokedAtUtc,
            })
            .ToListAsync(cancellationToken);

        return new AdminUserDetail
        {
            Summary = summary with { ManagedEventCount = managedEventCount },
            Profile = profile,
            ManagedEventCount = managedEventCount,
            Licenses = licenses,
            CalendarConnection = connection,
        };
    }

    /// <summary>
    /// The one directory projection, used by both the list and the detail so the two can never
    /// describe the same account differently.
    /// </summary>
    /// <remarks>
    /// "Active" means a redeemed license exists; "Suspended" means one was revoked and none is
    /// redeemed. The state is derived from the license rows on every read rather than stored, which
    /// is why a revocation takes effect with no sweep (ADR-095). <c>ManagedEventCount</c> is left at
    /// zero here and filled in by the caller from a single grouped query, because a correlated count
    /// per row would issue one subquery per listed account.
    /// </remarks>
    private IQueryable<AdminUserListItem> Project(IQueryable<User> users) =>
        users.Select(user => new AdminUserListItem
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            LicenseState = dbContext.Licenses.Any(license =>
                license.RedeemedByUserId == user.Id
                && license.Status == LicenseStatus.Redeemed)
                    ? UserLicenseState.Active
                    : dbContext.Licenses.Any(license =>
                        license.RedeemedByUserId == user.Id
                        && license.Status == LicenseStatus.Revoked)
                        ? UserLicenseState.Suspended
                        : UserLicenseState.None,
            HasProfile = dbContext.StudentProfiles.Any(profile => profile.UserId == user.Id),
            AcademicYear = dbContext.StudentProfiles
                .Where(profile => profile.UserId == user.Id)
                .Select(profile => profile.AcademicYear)
                .FirstOrDefault(),
            ClassYear = dbContext.StudentProfiles
                .Where(profile => profile.UserId == user.Id)
                .Select(profile => (int?)profile.ClassYear)
                .FirstOrDefault(),
            ProgramLanguage = dbContext.StudentProfiles
                .Where(profile => profile.UserId == user.Id)
                .Select(profile => (ProgramLanguage?)profile.ProgramLanguage)
                .FirstOrDefault(),
            StudentNumber = dbContext.StudentProfiles
                .Where(profile => profile.UserId == user.Id)
                .Select(profile => profile.StudentNumber)
                .FirstOrDefault(),
            CalendarStatus = dbContext.GoogleCalendarConnections
                .Where(connection => connection.UserId == user.Id)
                .Select(connection => (GoogleCalendarConnectionStatus?)connection.Status)
                .FirstOrDefault(),
            InitialSyncState = dbContext.GoogleCalendarConnections
                .Where(connection => connection.UserId == user.Id)
                .Select(connection => (GoogleCalendarInitialSyncState?)connection.InitialSyncState)
                .FirstOrDefault(),
            ManagedEventCount = 0,
            CreatedAtUtc = user.CreatedAtUtc,
            LastSignedInAtUtc = user.LastSignedInAtUtc,
        });

    private IQueryable<User> ApplyIdentityFilters(IQueryable<User> users, AdminUserQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim();

            // Uppercased the way User stores NormalizedEmail, but *not* through
            // User.NormalizeEmailValue: that validates a whole address and throws, so any partial
            // term an operator actually types ("zeyn", "@ogr") would have been an unhandled 500.
            string normalizedEmail = term.ToUpperInvariant();
            string pattern = $"%{Escape(term)}%";

            users = users.Where(user =>
                user.NormalizedEmail.Contains(normalizedEmail)
                || (user.DisplayName != null
                    && EF.Functions.ILike(user.DisplayName, pattern, "\\"))
                || dbContext.StudentProfiles.Any(profile =>
                    profile.UserId == user.Id
                    && profile.StudentNumber.StartsWith(term)));
        }

        if (query.Role is { } role)
        {
            users = users.Where(user => user.Role == role);
        }

        if (query.CreatedFromUtc is { } createdFrom)
        {
            users = users.Where(user => user.CreatedAtUtc >= createdFrom);
        }

        if (query.CreatedToUtc is { } createdTo)
        {
            users = users.Where(user => user.CreatedAtUtc <= createdTo);
        }

        if (query.LastSignedInFromUtc is { } signedInFrom)
        {
            users = users.Where(user => user.LastSignedInAtUtc >= signedInFrom);
        }

        if (query.LastSignedInToUtc is { } signedInTo)
        {
            users = users.Where(user => user.LastSignedInAtUtc <= signedInTo);
        }

        return users;
    }

    private IQueryable<User> ApplyProfileFilters(IQueryable<User> users, AdminUserQuery query)
    {
        if (query.HasProfile is { } hasProfile)
        {
            users = hasProfile
                ? users.Where(user =>
                    dbContext.StudentProfiles.Any(profile => profile.UserId == user.Id))
                : users.Where(user =>
                    !dbContext.StudentProfiles.Any(profile => profile.UserId == user.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.AcademicYear))
        {
            string academicYear = query.AcademicYear.Trim();
            users = users.Where(user => dbContext.StudentProfiles.Any(profile =>
                profile.UserId == user.Id && profile.AcademicYear == academicYear));
        }

        if (query.ClassYear is { } classYear)
        {
            users = users.Where(user => dbContext.StudentProfiles.Any(profile =>
                profile.UserId == user.Id && profile.ClassYear == classYear));
        }

        if (query.ProgramLanguage is { } programLanguage)
        {
            users = users.Where(user => dbContext.StudentProfiles.Any(profile =>
                profile.UserId == user.Id && profile.ProgramLanguage == programLanguage));
        }

        return users;
    }

    private IQueryable<User> ApplyCalendarFilters(IQueryable<User> users, AdminUserQuery query)
    {
        if (query.HasCalendarConnection is { } hasConnection)
        {
            users = hasConnection
                ? users.Where(user => dbContext.GoogleCalendarConnections
                    .Any(connection => connection.UserId == user.Id))
                : users.Where(user => !dbContext.GoogleCalendarConnections
                    .Any(connection => connection.UserId == user.Id));
        }

        if (query.CalendarStatus is { } status)
        {
            users = users.Where(user => dbContext.GoogleCalendarConnections.Any(connection =>
                connection.UserId == user.Id && connection.Status == status));
        }

        if (query.InitialSyncState is { } syncState)
        {
            users = users.Where(user => dbContext.GoogleCalendarConnections.Any(connection =>
                connection.UserId == user.Id && connection.InitialSyncState == syncState));
        }

        return users;
    }

    private IQueryable<User> ApplyLicenseFilter(IQueryable<User> users, UserLicenseState? state) =>
        state switch
        {
            UserLicenseState.Active => users.Where(user => dbContext.Licenses.Any(license =>
                license.RedeemedByUserId == user.Id && license.Status == LicenseStatus.Redeemed)),
            UserLicenseState.Suspended => users.Where(user =>
                !dbContext.Licenses.Any(license =>
                    license.RedeemedByUserId == user.Id && license.Status == LicenseStatus.Redeemed)
                && dbContext.Licenses.Any(license =>
                    license.RedeemedByUserId == user.Id && license.Status == LicenseStatus.Revoked)),
            UserLicenseState.None => users.Where(user => !dbContext.Licenses.Any(license =>
                license.RedeemedByUserId == user.Id
                && (license.Status == LicenseStatus.Redeemed
                    || license.Status == LicenseStatus.Revoked))),
            _ => users,
        };

    /// <summary>
    /// The accounts whose profile carries every requested selector, or null when no selector filter
    /// was asked for. Returning null rather than an empty set keeps "no filter" distinguishable
    /// from "filtered to nothing".
    /// </summary>
    private async Task<HashSet<Guid>?> ApplySelectorFilterAsync(
        AdminUserQuery query,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> required = (query.Selectors ?? new Dictionary<string, string>())
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal);

        if (required.Count == 0)
        {
            return null;
        }

        // Narrowed by the profile dimensions that *are* translatable before materializing, so the
        // in-memory pass reads one cohort rather than every profile in the database.
        IQueryable<StudentProfile> profiles = dbContext.StudentProfiles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.AcademicYear))
        {
            string academicYear = query.AcademicYear.Trim();
            profiles = profiles.Where(profile => profile.AcademicYear == academicYear);
        }

        if (query.ClassYear is { } classYear)
        {
            profiles = profiles.Where(profile => profile.ClassYear == classYear);
        }

        if (query.ProgramLanguage is { } programLanguage)
        {
            profiles = profiles.Where(profile => profile.ProgramLanguage == programLanguage);
        }

        List<StudentProfile> candidates = await profiles.ToListAsync(cancellationToken);

        return
        [
            .. candidates
                .Where(profile => required.All(pair =>
                    profile.Selectors.TryGetValue(pair.Key, out string? value)
                    && string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase)))
                .Select(profile => profile.UserId),
        ];
    }

    private static IQueryable<User> Sort(IQueryable<User> users, AdminUserQuery query) =>
        (query.Sort, query.Descending) switch
        {
            (AdminUserSort.LastSignedInAtUtc, true) =>
                users.OrderByDescending(user => user.LastSignedInAtUtc).ThenBy(user => user.Id),
            (AdminUserSort.LastSignedInAtUtc, false) =>
                users.OrderBy(user => user.LastSignedInAtUtc).ThenBy(user => user.Id),
            (AdminUserSort.Email, true) =>
                users.OrderByDescending(user => user.NormalizedEmail).ThenBy(user => user.Id),
            (AdminUserSort.Email, false) =>
                users.OrderBy(user => user.NormalizedEmail).ThenBy(user => user.Id),
            (_, false) => users.OrderBy(user => user.CreatedAtUtc).ThenBy(user => user.Id),
            _ => users.OrderByDescending(user => user.CreatedAtUtc).ThenBy(user => user.Id),
        };

    private async Task<Dictionary<Guid, int>> ManagedEventCountsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .Where(mapping => userIds.Contains(mapping.UserId))
            .GroupBy(mapping => mapping.UserId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.UserId, row => row.Count, cancellationToken);
    }

    /// <summary>Escapes the two LIKE wildcards so a searched underscore matches an underscore.</summary>
    private static string Escape(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
