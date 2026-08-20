using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Infrastructure.Persistence.Identity.Stores;

/// <summary>
/// Erases one account and anonymizes its cross-cutting audit trail in a single transaction
/// (ADR-118).
/// </summary>
/// <remarks>
/// The personal aggregates — the student profile, the Calendar connection and its encrypted token,
/// the event-mapping ledger, the department-colour preferences, and any single-user announcement
/// addressed to them and its deliveries — are removed by database <c>ON DELETE CASCADE</c> when the
/// user row is deleted, so deleting the user is what deletes them.
/// <para>
/// The tables whose foreign key to the user is <c>RESTRICT</c> are handled explicitly, because a
/// cascade would either destroy history that must survive or is deliberately not configured:
/// </para>
/// <list type="bullet">
/// <item>
/// the cross-cutting <c>audit_events</c> log is kept, with the deleted person's identifying fields
/// (actor id, actor e-mail, both IP forms, user agent) cleared — this is the "anonymize the trail"
/// half of the data policy, and nulling the actor id is also what releases the <c>RESTRICT</c> link
/// so the user row can be removed;
/// </item>
/// <item>
/// the <c>licenses</c> the person redeemed or revoked are kept — a redeemed single-use code must
/// stay unusable — but their link to the now-deleted user is nulled, so the row survives as an
/// anonymized fact that a redemption happened;
/// </item>
/// <item>
/// the person's own <c>license_audits</c> rows (whose actor id cannot be null) are the record of
/// <em>them</em> acting, so they are removed as part of the erasure; the surviving detached license
/// row and the <c>AccountDeleted</c> event keep the platform-level fact.
/// </item>
/// </list>
/// </remarks>
public sealed class AccountDeletionStore(SirkadiyenDbContext dbContext) : IAccountDeletionStore
{
    public Task<AccountCalendarCleanup?> GetCalendarCleanupAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.GoogleCalendarConnections
            .AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .Select(connection => new AccountCalendarCleanup
            {
                ProtectedRefreshToken = connection.ProtectedRefreshToken,
                ManagedCalendarId = connection.ManagedCalendarId,
                Status = connection.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);

    public Task<AccountDeletionStoreResult> DeleteAsync(
        Guid userId,
        AuditEvent accountDeletedEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountDeletedEvent);

        return RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            bool exists = await dbContext.Users
                .AnyAsync(user => user.Id == userId, cancellationToken);
            if (!exists)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AccountDeletionStoreResult { Deleted = false };
            }

            // 1. Record that the account was deleted, while the user still exists so the actor
            //    foreign key is valid at insert time.
            dbContext.AuditEvents.Add(accountDeletedEvent);
            await dbContext.SaveChangesAsync(cancellationToken);

            // 2. Anonymize the cross-cutting audit trail this person acted in — including the record
            //    just added for a self-deletion. Nulling the actor id both erases the identity and
            //    releases the RESTRICT foreign key that would otherwise block the user delete.
            int anonymizedAuditEvents = await dbContext.AuditEvents
                .Where(auditEvent => auditEvent.ActorUserId == userId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(auditEvent => auditEvent.ActorUserId, (Guid?)null)
                        .SetProperty(auditEvent => auditEvent.ActorEmail, (string?)null)
                        .SetProperty(auditEvent => auditEvent.MaskedIp, (string?)null)
                        .SetProperty(auditEvent => auditEvent.ProtectedIp, (string?)null)
                        .SetProperty(auditEvent => auditEvent.UserAgent, (string?)null),
                    cancellationToken);

            // 3. Detach the licensing rows that point at this user (RESTRICT), keeping the rows.
            int detachedLicenses = await dbContext.Licenses
                .Where(license => license.RedeemedByUserId == userId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(license => license.RedeemedByUserId, (Guid?)null),
                    cancellationToken);
            detachedLicenses += await dbContext.Licenses
                .Where(license => license.RevokedByUserId == userId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(license => license.RevokedByUserId, (Guid?)null),
                    cancellationToken);

            // 4. Remove the erased subject's own license-audit rows (RESTRICT, actor id non-null).
            int deletedLicenseAudits = await dbContext.LicenseAudits
                .Where(audit => audit.ActorUserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            // 5. Delete the user; the database cascades every personal aggregate.
            await dbContext.Users
                .Where(user => user.Id == userId)
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new AccountDeletionStoreResult
            {
                Deleted = true,
                AnonymizedAuditEvents = anonymizedAuditEvents,
                DetachedLicenses = detachedLicenses,
                DeletedLicenseAudits = deletedLicenseAudits,
            };
        });
    }
}
