using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Administration;

/// <summary>
/// Read-only admin views over user accounts: a filterable, sortable, paged list and a per-user
/// detail that composes identity, profile, license history, Calendar connection and managed-event
/// count. All projections are safe for an administrator to see and never expose credentials,
/// refresh tokens or license code hashes.
/// </summary>
public interface IAdminUserReadStore
{
    Task<PagedResult<AdminUserListItem>> ListAsync(
        AdminUserQuery query,
        CancellationToken cancellationToken);

    Task<AdminUserDetail?> FindAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// The filters an operator can combine over the account directory.
/// </summary>
/// <remarks>
/// Every filter here is a fact the backend already stores. Nothing is inferred: an absent value
/// means "do not filter on this", never "assume a default", so a narrower result set is always
/// explained by a filter the operator actually chose.
/// </remarks>
public sealed record AdminUserQuery
{
    /// <summary>
    /// Case-insensitive free-text filter matched against the e-mail address, the display name and
    /// the student number. A student number is matched as a prefix, because the operator usually
    /// reads the leading faculty digits off a list rather than the whole number.
    /// </summary>
    public string? Search { get; init; }

    public UserRole? Role { get; init; }

    /// <summary>Derived from license history, not stored on the user (see the store).</summary>
    public UserLicenseState? LicenseState { get; init; }

    /// <summary>Whether an academic profile exists at all.</summary>
    public bool? HasProfile { get; init; }

    public string? AcademicYear { get; init; }

    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    /// <summary>
    /// Academic-profile selector values that must all match, e.g. <c>practiceGroup=A</c> plus
    /// <c>anatomyGroup=2</c>. An empty or null dictionary applies no selector filter.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Selectors { get; init; }

    /// <summary>Whether a Google Calendar authorization exists at all.</summary>
    public bool? HasCalendarConnection { get; init; }

    public GoogleCalendarConnectionStatus? CalendarStatus { get; init; }

    public GoogleCalendarInitialSyncState? InitialSyncState { get; init; }

    public DateTimeOffset? CreatedFromUtc { get; init; }

    public DateTimeOffset? CreatedToUtc { get; init; }

    public DateTimeOffset? LastSignedInFromUtc { get; init; }

    public DateTimeOffset? LastSignedInToUtc { get; init; }

    public AdminUserSort Sort { get; init; } = AdminUserSort.CreatedAtUtc;

    public bool Descending { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public enum AdminUserSort
{
    CreatedAtUtc,
    LastSignedInAtUtc,
    Email,
}

public sealed record AdminUserListItem
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    public required UserRole Role { get; init; }

    public required UserLicenseState LicenseState { get; init; }

    public required bool HasProfile { get; init; }

    /// <summary>The profile's academic year, or null when there is no profile.</summary>
    public string? AcademicYear { get; init; }

    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    public string? StudentNumber { get; init; }

    /// <summary>Null when the account has never authorized Calendar access.</summary>
    public GoogleCalendarConnectionStatus? CalendarStatus { get; init; }

    public GoogleCalendarInitialSyncState? InitialSyncState { get; init; }

    /// <summary>How many events the mapping ledger says are on this user's managed calendar.</summary>
    public required int ManagedEventCount { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset LastSignedInAtUtc { get; init; }
}

public sealed record AdminUserDetail
{
    public required AdminUserListItem Summary { get; init; }

    public AdminUserProfile? Profile { get; init; }

    public required int ManagedEventCount { get; init; }

    public required IReadOnlyList<AdminUserLicense> Licenses { get; init; }

    /// <summary>The Calendar authorization, or null when the user never granted one.</summary>
    public AdminUserCalendarConnection? CalendarConnection { get; init; }
}

public sealed record AdminUserProfile
{
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string StudentNumber { get; init; }

    public required string SelectorSchemaVersion { get; init; }

    public required IReadOnlyDictionary<string, string> Selectors { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>
/// What an operator may know about a user's Calendar authorization. The protected refresh token
/// and the granted scopes are deliberately absent: neither helps an operator and one is a
/// credential (AI_GUIDELINE §15).
/// </summary>
public sealed record AdminUserCalendarConnection
{
    public required GoogleCalendarConnectionStatus Status { get; init; }

    public required GoogleCalendarInitialSyncState InitialSyncState { get; init; }

    public required bool HasManagedCalendar { get; init; }

    public DateTimeOffset? ManagedCalendarUnavailableAtUtc { get; init; }

    public DateTimeOffset? LastCalendarInventoryAtUtc { get; init; }

    /// <summary>Set while a profile change is waiting for the re-synchronization stage (ADR-096).</summary>
    public DateTimeOffset? ProfileResyncRequiredSinceUtc { get; init; }

    /// <summary>Set while a dead credential's missed diffs are waiting for replay (ADR-060).</summary>
    public DateTimeOffset? ReconciliationRequiredSinceUtc { get; init; }
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
