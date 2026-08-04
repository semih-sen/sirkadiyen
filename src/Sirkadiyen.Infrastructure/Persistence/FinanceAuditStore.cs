using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>Read-only access to the append-only finance audit log.</summary>
public sealed class FinanceAuditStore(SirkadiyenDbContext dbContext) : IFinanceAuditStore
{
    private const int MaximumPageSize = 200;

    public async Task<PagedResult<FinanceAuditListItem>> ListAsync(
        FinanceAuditQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);

        IQueryable<FinanceAudit> audits = dbContext.FinanceAudits.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.SubjectType))
        {
            audits = audits.Where(audit => audit.SubjectType == query.SubjectType);
        }

        if (query.SubjectId is { } subjectId)
        {
            audits = audits.Where(audit => audit.SubjectId == subjectId);
        }

        if (query.Action is { } action)
        {
            audits = audits.Where(audit => audit.Action == action);
        }

        if (query.ActorUserId is { } actorUserId)
        {
            audits = audits.Where(audit => audit.ActorUserId == actorUserId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            audits = audits.Where(audit => audit.OccurredAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            audits = audits.Where(audit => audit.OccurredAtUtc <= toUtc);
        }

        int totalCount = await audits.CountAsync(cancellationToken);

        List<FinanceAuditListItem> items = await audits
            .OrderByDescending(audit => audit.Sequence)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(audit => Project(audit))
            .ToListAsync(cancellationToken);

        return new PagedResult<FinanceAuditListItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<IReadOnlyList<FinanceAuditDetail>> GetHistoryAsync(
        string subjectType,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        List<FinanceAudit> audits = await dbContext.FinanceAudits
            .AsNoTracking()
            .Where(audit => audit.SubjectType == subjectType && audit.SubjectId == subjectId)
            .OrderBy(audit => audit.Sequence)
            .ToListAsync(cancellationToken);

        return
        [
            .. audits.Select(audit => new FinanceAuditDetail
            {
                Summary = Project(audit),
                BeforeState = audit.BeforeState,
                AfterState = audit.AfterState,
            }),
        ];
    }

    private static FinanceAuditListItem Project(FinanceAudit audit) => new()
    {
        Sequence = audit.Sequence,
        Action = audit.Action,
        SubjectType = audit.SubjectType,
        SubjectId = audit.SubjectId,
        ActorUserId = audit.ActorUserId,
        ActorEmail = audit.ActorEmail,
        OccurredAtUtc = audit.OccurredAtUtc,
        CorrelationId = audit.CorrelationId,
        Reason = audit.Reason,
        AmountDelta = audit.AmountDelta,
        RevisionNumber = audit.RevisionNumber,
        ChangedFields = audit.ChangedFields,
    };
}
