using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Read-only admin listing and detail over licenses. It never projects the code hash: a license is
/// addressed by id, and the plaintext code was shown only once at creation (AI_GUIDELINE §7).
/// </summary>
public sealed class AdminLicenseReadStore(SirkadiyenDbContext dbContext) : IAdminLicenseReadStore
{
    private const int MaximumPageSize = 200;

    public async Task<PagedResult<AdminLicenseListItem>> ListAsync(
        AdminLicenseQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);

        IQueryable<License> licenses = dbContext.Licenses.AsNoTracking();

        if (query.Status is { } status)
        {
            licenses = licenses.Where(license => license.Status == status);
        }

        if (query.Kind is { } kind)
        {
            licenses = licenses.Where(license => license.Kind == kind);
        }

        int totalCount = await licenses.CountAsync(cancellationToken);

        List<AdminLicenseListItem> items = await licenses
            .OrderByDescending(license => license.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(license => Project(license))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminLicenseListItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<AdminLicenseDetail?> FindAsync(
        Guid licenseId,
        CancellationToken cancellationToken)
    {
        AdminLicenseListItem? summary = await dbContext.Licenses
            .AsNoTracking()
            .Where(license => license.Id == licenseId)
            .Select(license => Project(license))
            .SingleOrDefaultAsync(cancellationToken);

        if (summary is null)
        {
            return null;
        }

        List<AdminLicenseAuditEntry> audit = await dbContext.LicenseAudits
            .AsNoTracking()
            .Where(entry => entry.LicenseId == licenseId)
            .OrderBy(entry => entry.OccurredAtUtc)
            .Select(entry => new AdminLicenseAuditEntry
            {
                Action = entry.Action,
                ActorEmail = entry.ActorEmail,
                Reason = entry.Reason,
                OccurredAtUtc = entry.OccurredAtUtc,
            })
            .ToListAsync(cancellationToken);

        return new AdminLicenseDetail { Summary = summary, Audit = audit };
    }

    // Static so the projection is translated in SQL and can never accidentally read CodeHash.
    private static AdminLicenseListItem Project(License license) => new()
    {
        LicenseId = license.Id,
        Kind = license.Kind,
        Status = license.Status,
        CreatedByEmail = license.CreatedByEmail,
        CreatedAtUtc = license.CreatedAtUtc,
        ExpiresAtUtc = license.ExpiresAtUtc,
        RedeemedByUserId = license.RedeemedByUserId,
        RedeemedAtUtc = license.RedeemedAtUtc,
        RevokedAtUtc = license.RevokedAtUtc,
        Notes = license.Notes,
    };
}
