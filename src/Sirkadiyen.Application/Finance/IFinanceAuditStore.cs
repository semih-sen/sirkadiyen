using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

public sealed record FinanceAuditListItem
{
    public required long Sequence { get; init; }

    public required FinanceAuditAction Action { get; init; }

    public required string SubjectType { get; init; }

    public required Guid SubjectId { get; init; }

    public required Guid ActorUserId { get; init; }

    public required string ActorEmail { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public string? CorrelationId { get; init; }

    public string? Reason { get; init; }

    public required decimal AmountDelta { get; init; }

    public required int RevisionNumber { get; init; }

    public required IReadOnlyList<string> ChangedFields { get; init; }
}

public sealed record FinanceAuditDetail
{
    public required FinanceAuditListItem Summary { get; init; }

    public string? BeforeState { get; init; }

    public string? AfterState { get; init; }
}

public sealed record FinanceAuditQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public string? SubjectType { get; init; }

    public Guid? SubjectId { get; init; }

    public FinanceAuditAction? Action { get; init; }

    public Guid? ActorUserId { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }
}

/// <summary>
/// Read-only access to the finance module's own append-only audit log. No update or delete method
/// exists here on purpose — <c>finance_audits</c> is append-only (ADR-092 §3).
/// </summary>
public interface IFinanceAuditStore
{
    Task<PagedResult<FinanceAuditListItem>> ListAsync(
        FinanceAuditQuery query,
        CancellationToken cancellationToken);

    /// <summary>The full audit chain for one subject (for example, one transaction), in order.</summary>
    Task<IReadOnlyList<FinanceAuditDetail>> GetHistoryAsync(
        string subjectType,
        Guid subjectId,
        CancellationToken cancellationToken);
}
