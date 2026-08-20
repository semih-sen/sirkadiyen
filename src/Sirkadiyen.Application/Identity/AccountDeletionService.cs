using System.Text.Json;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.Identity;

namespace Sirkadiyen.Application.Identity;

/// <summary>
/// Permanently deletes an account, on its owner's request or a SuperAdmin's, together with the
/// external grant and calendar it created (ADR-118).
/// </summary>
/// <remarks>
/// One service for both doors on purpose, exactly like <see cref="GoogleCalendar.ManagedCalendarRebuildService"/>:
/// a student deletes their own account from their panel, an operator deletes it for them, and the
/// eligibility rule, the external cleanup, the erasure and the audit record must not be able to
/// differ between the two.
/// <para>
/// The order matters. The external Google cleanup runs <em>first</em> and best-effort, outside any
/// database transaction (systemPatterns §16 forbids an external call inside one): a dead token or an
/// already-deleted calendar must not stop a person from being erased. Its outcome is recorded in the
/// audit metadata so "was their Google calendar actually removed" is answerable later. The database
/// erasure then runs as one transaction that also appends the single
/// <see cref="AuditEventCategory.AccountDeleted"/> record, so a deletion is atomic and always leaves
/// exactly that trail.
/// </para>
/// <para>
/// A <see cref="UserRole.SuperAdmin"/> is refused. The bootstrap operator is re-granted the role on
/// every sign-in (ADR-045), and deleting the only administrator would strand the system; a
/// destructive account whose loss cannot be undone is not something either door should be able to do
/// to the operator.
/// </para>
/// </remarks>
public sealed class AccountDeletionService(
    IUserStore userStore,
    IAccountDeletionStore deletionStore,
    IExternalAccountCleanup externalCleanup,
    IAuditIpProtector ipProtector,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions MetadataOptions = ContractJson.CreateOptions();

    public async Task<AccountDeletionResult> DeleteAsync(
        AccountDeletionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        UserSession? user = await userStore.FindSessionAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return new AccountDeletionResult { Outcome = AccountDeletionOutcome.UserNotFound };
        }

        if (user.Role is UserRole.SuperAdmin)
        {
            return new AccountDeletionResult { Outcome = AccountDeletionOutcome.SuperAdminRefused };
        }

        // The confirmation phrase is the account's own e-mail, typed by whoever is deleting it: the
        // owner confirms it is really their account, and an operator confirms they are deleting the
        // person they mean to (§30's "confirm the subject's own identifier when there is one"). Both
        // are checked against the target's stored e-mail, so a mistyped id deletes nobody.
        if (!EmailsMatch(user.Email, request.ConfirmEmail))
        {
            return new AccountDeletionResult { Outcome = AccountDeletionOutcome.EmailMismatch };
        }

        ExternalAccountCleanupResult cleanup = await CleanUpExternalAsync(
            request.UserId,
            cancellationToken);

        AuditEvent auditEvent = BuildAuditEvent(request, cleanup);

        AccountDeletionStoreResult stored = await deletionStore.DeleteAsync(
            request.UserId,
            auditEvent,
            cancellationToken);

        if (!stored.Deleted)
        {
            // The user existed a moment ago; a concurrent deletion won the race. Report it the same
            // way as a missing user rather than claiming this call did the deletion.
            return new AccountDeletionResult { Outcome = AccountDeletionOutcome.UserNotFound };
        }

        return new AccountDeletionResult
        {
            Outcome = AccountDeletionOutcome.Deleted,
            HadManagedCalendar = cleanup.HadManagedCalendar,
            GoogleCalendarDeleted = cleanup.CalendarDeleted,
            GoogleTokenRevoked = cleanup.TokenRevoked,
            AnonymizedAuditEvents = stored.AnonymizedAuditEvents,
        };
    }

    private async Task<ExternalAccountCleanupResult> CleanUpExternalAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        AccountCalendarCleanup? credential =
            await deletionStore.GetCalendarCleanupAsync(userId, cancellationToken);

        return credential is null
            ? new ExternalAccountCleanupResult
            {
                HadManagedCalendar = false,
                CalendarDeleted = false,
                TokenRevoked = false,
            }
            : await externalCleanup.CleanUpAsync(credential, cancellationToken);
    }

    private AuditEvent BuildAuditEvent(
        AccountDeletionRequest request,
        ExternalAccountCleanupResult cleanup)
    {
        string? maskedIp = AuditIp.Mask(request.ClientIp);
        string? protectedIp = maskedIp is null ? null : ipProtector.Protect(request.ClientIp!);

        string metadata = JsonSerializer.Serialize(
            new
            {
                requestedBy = request.RequestedByOperator ? "operator" : "self",
                hadManagedCalendar = cleanup.HadManagedCalendar,
                googleCalendarDeleted = cleanup.CalendarDeleted,
                googleTokenRevoked = cleanup.TokenRevoked,
            },
            MetadataOptions);

        // The subject is the account being erased; the actor is whoever authorized it. For a
        // self-deletion the two are the same, and the store's anonymization then clears this actor
        // along with the rest of that person's trail — leaving the subject id, reason and metadata.
        return AuditEvent.Create(
            AuditEventCategory.AccountDeleted,
            timeProvider.GetUtcNow(),
            request.ActorUserId,
            request.ActorEmail,
            "User",
            request.UserId.ToString(),
            request.CorrelationId,
            maskedIp,
            protectedIp,
            request.UserAgent,
            request.Reason,
            metadata);
    }

    private static bool EmailsMatch(string accountEmail, string? confirmEmail)
    {
        if (string.IsNullOrWhiteSpace(confirmEmail))
        {
            return false;
        }

        try
        {
            return string.Equals(
                User.NormalizeEmailValue(accountEmail),
                User.NormalizeEmailValue(confirmEmail),
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // A confirmation that is not even a valid e-mail address never matches.
            return false;
        }
    }
}

/// <summary>A request to permanently delete one account (ADR-118).</summary>
public sealed record AccountDeletionRequest
{
    /// <summary>The account to delete.</summary>
    public required Guid UserId { get; init; }

    /// <summary>True when a SuperAdmin authorized it; false when the owner asked to be deleted.</summary>
    public required bool RequestedByOperator { get; init; }

    /// <summary>Who authorized it: the operator, or the owner themselves for a self-deletion.</summary>
    public required Guid ActorUserId { get; init; }

    public required string ActorEmail { get; init; }

    /// <summary>
    /// The account's own e-mail, retyped as the confirmation phrase. Compared against the target's
    /// stored e-mail; a deletion with a mismatched or missing value is refused.
    /// </summary>
    public string? ConfirmEmail { get; init; }

    /// <summary>Required for an operator deletion; a self-deletion needs no stated reason.</summary>
    public string? Reason { get; init; }

    public string? CorrelationId { get; init; }

    public string? ClientIp { get; init; }

    public string? UserAgent { get; init; }
}

public sealed record AccountDeletionResult
{
    public required AccountDeletionOutcome Outcome { get; init; }

    public bool HadManagedCalendar { get; init; }

    public bool GoogleCalendarDeleted { get; init; }

    public bool GoogleTokenRevoked { get; init; }

    public int AnonymizedAuditEvents { get; init; }
}

public enum AccountDeletionOutcome
{
    /// <summary>The account and its personal data are gone; the audit trail is anonymized.</summary>
    Deleted,

    /// <summary>No account with the id exists.</summary>
    UserNotFound,

    /// <summary>The account is a SuperAdmin and cannot be deleted through this flow.</summary>
    SuperAdminRefused,

    /// <summary>The confirmation e-mail did not match the account being deleted.</summary>
    EmailMismatch,
}
