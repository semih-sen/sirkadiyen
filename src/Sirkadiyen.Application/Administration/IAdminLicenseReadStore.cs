using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Licensing;

namespace Sirkadiyen.Application.Administration;

/// <summary>
/// Read-only admin views over licenses: a filtered, paged listing and a per-license detail with its
/// audit trail. The code hash is never projected — a license is identified by id, never by code.
/// </summary>
public interface IAdminLicenseReadStore
{
    Task<PagedResult<AdminLicenseListItem>> ListAsync(
        AdminLicenseQuery query,
        CancellationToken cancellationToken);

    Task<AdminLicenseDetail?> FindAsync(Guid licenseId, CancellationToken cancellationToken);
}

public sealed record AdminLicenseQuery
{
    public LicenseStatus? Status { get; init; }

    public LicenseKind? Kind { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public sealed record AdminLicenseListItem
{
    public required Guid LicenseId { get; init; }

    public required LicenseKind Kind { get; init; }

    public required LicenseStatus Status { get; init; }

    public required string CreatedByEmail { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public Guid? RedeemedByUserId { get; init; }

    public DateTimeOffset? RedeemedAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record AdminLicenseDetail
{
    public required AdminLicenseListItem Summary { get; init; }

    public required IReadOnlyList<AdminLicenseAuditEntry> Audit { get; init; }
}

public sealed record AdminLicenseAuditEntry
{
    public required LicenseAuditAction Action { get; init; }

    public required string ActorEmail { get; init; }

    public required string Reason { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}
