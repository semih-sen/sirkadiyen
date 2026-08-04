using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>Composes the admin user list and detail from identity, profile, and license tables.</summary>
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

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string normalized = User.NormalizeEmailValue(query.Search.Trim());
            users = users.Where(user => user.NormalizedEmail.Contains(normalized));
        }

        if (query.Role is { } role)
        {
            users = users.Where(user => user.Role == role);
        }

        int totalCount = await users.CountAsync(cancellationToken);

        List<AdminUserListItem> items = await users
            .OrderByDescending(user => user.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new AdminUserListItem
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
                HasProfile = dbContext.StudentProfiles
                    .Any(profile => profile.UserId == user.Id),
                CreatedAtUtc = user.CreatedAtUtc,
                LastSignedInAtUtc = user.LastSignedInAtUtc,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminUserListItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<AdminUserDetail?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        AdminUserListItem? summary = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AdminUserListItem
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
                HasProfile = dbContext.StudentProfiles
                    .Any(profile => profile.UserId == user.Id),
                CreatedAtUtc = user.CreatedAtUtc,
                LastSignedInAtUtc = user.LastSignedInAtUtc,
            })
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
            };

        int managedEventCount = await dbContext.UserCalendarEventMappings
            .AsNoTracking()
            .CountAsync(mapping => mapping.UserId == userId, cancellationToken);

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
            Summary = summary,
            Profile = profile,
            ManagedEventCount = managedEventCount,
            Licenses = licenses,
        };
    }
}
