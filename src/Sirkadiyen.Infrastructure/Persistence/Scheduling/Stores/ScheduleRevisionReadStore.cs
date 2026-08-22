using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// Read-only projections of revisions and their validation findings.
/// </summary>
public sealed class ScheduleRevisionReadStore(SirkadiyenDbContext dbContext)
    : IScheduleRevisionReadStore
{
    public async Task<IReadOnlyList<ScheduleRevisionSummary>> ListByStateAsync(
        RevisionState state,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await dbContext.ScheduleRevisions
            .AsNoTracking()
            .Where(revision => revision.State == state)
            .OrderBy(revision => revision.CreatedAtUtc)
            .Take(limit)
            .Select(revision => new ScheduleRevisionSummary
            {
                RevisionId = revision.Id,
                SourceId = revision.SourceId.Value,
                State = revision.State,
                CreatedAtUtc = revision.CreatedAtUtc,
                RecordCount = revision.RecordCount,
                StateReason = revision.StateReason,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleRevisionSummary>> ListRecentAsync(
        int limit,
        string? sourceId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IQueryable<ScheduleRevision> query = dbContext.ScheduleRevisions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            string trimmed = sourceId.Trim();
            query = query.Where(revision => revision.SourceId.Value == trimmed);
        }

        return await query
            .OrderByDescending(revision => revision.CreatedAtUtc)
            .ThenByDescending(revision => revision.Id)
            .Take(limit)
            .Select(revision => new ScheduleRevisionSummary
            {
                RevisionId = revision.Id,
                SourceId = revision.SourceId.Value,
                State = revision.State,
                CreatedAtUtc = revision.CreatedAtUtc,
                RecordCount = revision.RecordCount,
                StateReason = revision.StateReason,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ScheduleRevisionDetail?> FindAsync(
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        ScheduleRevision? revision = await dbContext.ScheduleRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == revisionId, cancellationToken);

        if (revision is null)
        {
            return null;
        }

        List<RevisionFindingView> findings = await dbContext.RevisionValidationFindings
            .AsNoTracking()
            .Where(finding => finding.ScheduleRevisionId == revisionId)
            .OrderBy(finding => finding.CreatedAtUtc)
            .ThenBy(finding => finding.Rule)
            .Select(finding => new RevisionFindingView
            {
                Rule = finding.Rule,
                Severity = finding.Severity,
                Message = finding.Message,
                AffectedRecordCount = finding.AffectedRecordCount,
                CreatedAtUtc = finding.CreatedAtUtc,
                Detail = finding.Detail,
            })
            .ToListAsync(cancellationToken);

        return new ScheduleRevisionDetail
        {
            Summary = new ScheduleRevisionSummary
            {
                RevisionId = revision.Id,
                SourceId = revision.SourceId.Value,
                State = revision.State,
                CreatedAtUtc = revision.CreatedAtUtc,
                RecordCount = revision.RecordCount,
                StateReason = revision.StateReason,
            },
            Findings = findings,
            ApprovedBy = revision.ApprovedBy,
            ApprovalReason = revision.ApprovalReason,
            ApprovedAtUtc = revision.ApprovedAtUtc,
            PublishedAtUtc = revision.PublishedAtUtc,
            RejectedBy = revision.RejectedBy,
            RejectionReason = revision.RejectionReason,
            RejectedAtUtc = revision.RejectedAtUtc,
        };
    }
}
