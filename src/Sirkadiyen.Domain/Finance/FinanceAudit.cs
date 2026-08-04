namespace Sirkadiyen.Domain.Finance;

/// <summary>
/// The finance module's own append-only audit log (ADR-092 §3) — distinct from the cross-cutting
/// <c>audit_events</c> table. Every create, edit, delete, settlement, write-off and distribution
/// writes one row in the same commit as the change itself, carrying a full before/after image so a
/// deleted transaction is fully reconstructable. No update or delete method exists here on purpose:
/// this log is append-only.
/// </summary>
public sealed class FinanceAudit
{
    public const int MaximumSubjectTypeLength = 40;

    public const int MaximumActorEmailLength = 320;

    public const int MaximumCorrelationIdLength = 100;

    public const int MaximumReasonLength = 2000;

    /// <summary>
    /// Reason is required for every action that corrects, removes, or otherwise moves money outside
    /// ordinary creation — an operator must always be able to say why.
    /// </summary>
    private static readonly HashSet<FinanceAuditAction> ReasonRequiredActions =
    [
        FinanceAuditAction.AccountClosed,
        FinanceAuditAction.HolderDeactivated,
        FinanceAuditAction.TransactionUpdated,
        FinanceAuditAction.TransactionDeleted,
        FinanceAuditAction.ObligationSettlementCancelled,
        FinanceAuditAction.ObligationWrittenOff,
        FinanceAuditAction.ObligationCancelled,
        FinanceAuditAction.DistributionExecuted,
        FinanceAuditAction.DistributionReversed,
    ];

    private FinanceAudit()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    /// <summary>Monotonic order that survives same-millisecond writes; assigned by the database.</summary>
    public long Sequence { get; private init; }

    public FinanceAuditAction Action { get; private init; }

    /// <summary>What this audit row is about. No FK — subjects are deletable.</summary>
    public string SubjectType { get; private init; } = string.Empty;

    public Guid SubjectId { get; private init; }

    public Guid ActorUserId { get; private init; }

    public string ActorEmail { get; private init; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private init; }

    public string? CorrelationId { get; private init; }

    public string? Reason { get; private init; }

    /// <summary>The complete prior row including its ledger entries, serialized as JSON. Null on create.</summary>
    public string? BeforeState { get; private init; }

    /// <summary>The complete resulting row including its ledger entries, serialized as JSON. Null on delete.</summary>
    public string? AfterState { get; private init; }

    public IReadOnlyList<string> ChangedFields { get; private init; } = [];

    /// <summary>The net cash effect of this operation on the books.</summary>
    public decimal AmountDelta { get; private init; }

    public int RevisionNumber { get; private init; }

    public static FinanceAudit Create(
        FinanceAuditAction action,
        string subjectType,
        Guid subjectId,
        Guid actorUserId,
        string actorEmail,
        DateTimeOffset occurredAtUtc,
        string? correlationId,
        string? reason,
        string? beforeState,
        string? afterState,
        IReadOnlyList<string>? changedFields,
        decimal amountDelta,
        int revisionNumber)
    {
        subjectType = RequiredBounded(subjectType, MaximumSubjectTypeLength, nameof(subjectType));
        if (subjectId == Guid.Empty)
        {
            throw new ArgumentException("A subject is required.", nameof(subjectId));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }

        actorEmail = RequiredBounded(actorEmail, MaximumActorEmailLength, nameof(actorEmail));
        correlationId = OptionalBounded(correlationId, MaximumCorrelationIdLength, nameof(correlationId));

        if (ReasonRequiredActions.Contains(action))
        {
            reason = RequiredBounded(reason, MaximumReasonLength, nameof(reason));
        }
        else
        {
            reason = OptionalBounded(reason, MaximumReasonLength, nameof(reason));
        }

        if (revisionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revisionNumber),
                revisionNumber,
                "A revision number must be at least 1.");
        }

        return new FinanceAudit
        {
            Id = Guid.CreateVersion7(),
            Action = action,
            SubjectType = subjectType,
            SubjectId = subjectId,
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId,
            Reason = reason,
            BeforeState = string.IsNullOrWhiteSpace(beforeState) ? null : beforeState,
            AfterState = string.IsNullOrWhiteSpace(afterState) ? null : afterState,
            ChangedFields = changedFields is null ? [] : [.. changedFields],
            AmountDelta = FinanceAmount.Require(amountDelta, nameof(amountDelta)),
            RevisionNumber = revisionNumber,
        };
    }

    private static string RequiredBounded(string? value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }

    private static string? OptionalBounded(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}

public enum FinanceAuditAction
{
    AccountOpened,
    AccountUpdated,
    AccountClosed,
    HolderCreated,
    HolderUpdated,
    HolderDeactivated,
    PartnerSharesChanged,
    TransactionCreated,
    TransactionUpdated,
    TransactionDeleted,
    ObligationCreated,
    ObligationUpdated,
    ObligationSettled,
    ObligationSettlementCancelled,
    ObligationWrittenOff,
    ObligationCancelled,
    DistributionExecuted,
    DistributionReversed,
}
