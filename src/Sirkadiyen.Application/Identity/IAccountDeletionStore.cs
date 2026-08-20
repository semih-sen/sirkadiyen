using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.GoogleCalendar;

namespace Sirkadiyen.Application.Identity;

/// <summary>
/// Erases one account's personal data and anonymizes its audit trail in a single transaction
/// (ADR-118).
/// </summary>
/// <remarks>
/// The chosen data policy is "erase the person, keep the anonymized trail": rows that are personal
/// data about the account (profile, Calendar connection and its encrypted token, event ledger,
/// colour preferences, announcement deliveries) are deleted, while the cross-cutting
/// <see cref="AuditEvent"/> log is kept with its identifying fields cleared so the history of what
/// happened on the platform survives without naming a deleted person (AI_GUIDELINE §19).
/// <para>
/// The deletion depends on database <c>ON DELETE CASCADE</c> for the personal aggregates, which is
/// why a plain delete of the user row removes them, and it explicitly handles the tables whose
/// foreign key to the user is <c>RESTRICT</c> — the cross-cutting audit log (anonymized) and the
/// licensing rows (detached, then the erased subject's own license-audit rows removed).
/// </para>
/// </remarks>
public interface IAccountDeletionStore
{
    /// <summary>
    /// Appends <paramref name="accountDeletedEvent"/>, anonymizes and detaches everything that
    /// references the user, then deletes the user row (cascading its personal aggregates), all in
    /// one transaction. A missing user is reported rather than thrown.
    /// </summary>
    Task<AccountDeletionStoreResult> DeleteAsync(
        Guid userId,
        AuditEvent accountDeletedEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns what the Google-side cleanup needs — the ciphertext refresh token, the managed
    /// calendar id and the credential status — or <see langword="null"/> when the user has no
    /// Calendar connection. This is a backend-only projection: like the worker's sync projections
    /// it carries the encrypted credential, which the read <c>GoogleCalendarConnectionView</c>
    /// deliberately never does (systemPatterns §25).
    /// </summary>
    Task<AccountCalendarCleanup?> GetCalendarCleanupAsync(
        Guid userId,
        CancellationToken cancellationToken);
}

/// <summary>
/// The credential and calendar id needed to delete a deleted account's managed Google calendar and
/// revoke its stored grant (ADR-118). Backend-only; the token lives only in memory for the call.
/// </summary>
public sealed record AccountCalendarCleanup
{
    public required string ProtectedRefreshToken { get; init; }

    /// <summary>Null when initial sync never created a calendar; then only the token is revoked.</summary>
    public string? ManagedCalendarId { get; init; }

    public required GoogleCalendarConnectionStatus Status { get; init; }
}

/// <summary>What the transactional erasure did, for the response and the audit metadata.</summary>
public sealed record AccountDeletionStoreResult
{
    /// <summary>False when no user with the id existed; nothing was changed.</summary>
    public required bool Deleted { get; init; }

    /// <summary>Cross-cutting audit-log rows whose actor identity was cleared.</summary>
    public int AnonymizedAuditEvents { get; init; }

    /// <summary>License rows the erased account had redeemed, which were deleted.</summary>
    public int DeletedLicenses { get; init; }

    /// <summary>License-audit rows removed with those licenses (and the subject's own actor rows).</summary>
    public int DeletedLicenseAudits { get; init; }
}
