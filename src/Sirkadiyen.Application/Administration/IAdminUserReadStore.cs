using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Administration;

/// <summary>
/// Read-only admin views over user accounts: a searchable, paged list and a per-user detail that
/// composes identity, profile, license history and managed-event count. All projections are safe
/// for an administrator to see and never expose credentials or license code hashes.
/// </summary>
public interface IAdminUserReadStore
{
    Task<PagedResult<AdminUserListItem>> ListAsync(
        AdminUserQuery query,
        CancellationToken cancellationToken);

    Task<AdminUserDetail?> FindAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record AdminUserQuery
{
    /// <summary>Case-insensitive email substring filter, or null for no email filter.</summary>
    public string? Search { get; init; }

    public UserRole? Role { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public sealed record AdminUserListItem
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    public required UserRole Role { get; init; }

    public required UserLicenseState LicenseState { get; init; }

    public required bool HasProfile { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset LastSignedInAtUtc { get; init; }
}

public sealed record AdminUserDetail
{
    public required AdminUserListItem Summary { get; init; }

    public AdminUserProfile? Profile { get; init; }

    public required int ManagedEventCount { get; init; }

    public required IReadOnlyList<AdminUserLicense> Licenses { get; init; }
}

public sealed record AdminUserProfile
{
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string StudentNumber { get; init; }

    public required string SelectorSchemaVersion { get; init; }

    public required IReadOnlyDictionary<string, string> Selectors { get; init; }
}

/// <summary>One license in a user's history. The code hash is never included.</summary>
public sealed record AdminUserLicense
{
    public required Guid LicenseId { get; init; }

    public required LicenseKind Kind { get; init; }

    public required LicenseStatus Status { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? RedeemedAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }
}
