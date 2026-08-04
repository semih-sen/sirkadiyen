using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Auditing;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>Append-only PostgreSQL store for account-access and activity events.</summary>
public sealed class AuditEventStore(SirkadiyenDbContext dbContext) : IAuditEventStore
{
    private const int MaximumPageSize = 200;

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        dbContext.AuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<AuditEventView>> QueryAsync(
        AuditEventQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);

        IQueryable<AuditEvent> filtered = dbContext.AuditEvents.AsNoTracking();

        if (query.Category is { } category)
        {
            filtered = filtered.Where(auditEvent => auditEvent.Category == category);
        }

        if (query.ActorUserId is { } actorUserId)
        {
            filtered = filtered.Where(auditEvent => auditEvent.ActorUserId == actorUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.ActorEmail))
        {
            string actorEmail = query.ActorEmail.Trim();
            filtered = filtered.Where(auditEvent => auditEvent.ActorEmail == actorEmail);
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectType))
        {
            string subjectType = query.SubjectType.Trim();
            filtered = filtered.Where(auditEvent => auditEvent.SubjectType == subjectType);
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectId))
        {
            string subjectId = query.SubjectId.Trim();
            filtered = filtered.Where(auditEvent => auditEvent.SubjectId == subjectId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            filtered = filtered.Where(auditEvent => auditEvent.OccurredAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            filtered = filtered.Where(auditEvent => auditEvent.OccurredAtUtc <= toUtc);
        }

        int totalCount = await filtered.CountAsync(cancellationToken);

        List<AuditEventView> items = await filtered
            .OrderByDescending(auditEvent => auditEvent.OccurredAtUtc)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(auditEvent => Project(auditEvent))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEventView>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<AuditEventView?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.Id == id)
            .Select(auditEvent => Project(auditEvent))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<string?> GetProtectedIpAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.Id == id)
            .Select(auditEvent => auditEvent.ProtectedIp)
            .SingleOrDefaultAsync(cancellationToken);

    // A static projection that never selects ProtectedIp, so the ciphertext cannot leak through a
    // read path. It only reports whether one exists.
    private static AuditEventView Project(AuditEvent auditEvent) => new()
    {
        Id = auditEvent.Id,
        Category = auditEvent.Category,
        OccurredAtUtc = auditEvent.OccurredAtUtc,
        ActorUserId = auditEvent.ActorUserId,
        ActorEmail = auditEvent.ActorEmail,
        SubjectType = auditEvent.SubjectType,
        SubjectId = auditEvent.SubjectId,
        CorrelationId = auditEvent.CorrelationId,
        MaskedIp = auditEvent.MaskedIp,
        HasProtectedIp = auditEvent.ProtectedIp != null,
        UserAgent = auditEvent.UserAgent,
        Reason = auditEvent.Reason,
        Metadata = auditEvent.Metadata,
    };
}
