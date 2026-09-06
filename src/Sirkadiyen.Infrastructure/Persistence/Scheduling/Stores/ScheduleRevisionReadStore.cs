using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// Read-only projections of revisions and their validation findings.
/// </summary>
/// <remarks>
/// Every listing goes through <see cref="Project"/>, so the review queue, the history view and the
/// detail view cannot drift into describing the same revision differently. The projection joins the
/// source and counts the findings in the database rather than in three round trips: a queue of
/// fifty revisions would otherwise be a hundred and fifty queries to say what each row is.
/// </remarks>
public sealed class ScheduleRevisionReadStore(SirkadiyenDbContext dbContext)
    : IScheduleRevisionReadStore
{
    public async Task<IReadOnlyList<ScheduleRevisionSummary>> ListByStateAsync(
        RevisionState state,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await Project(dbContext.ScheduleRevisions
                .AsNoTracking()
                .Where(revision => revision.State == state)
                .OrderBy(revision => revision.CreatedAtUtc)
                .Take(limit))
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
            // Compared as the value object, not as `SourceId.Value`. The column is mapped through
            // a value converter, so reaching inside it gives Entity Framework a property it cannot
            // translate and the whole query fails at runtime — which is what filtering the history
            // by source did until this was caught. An unparseable identifier matches nothing
            // rather than throwing: it is a query string, and a caller typing a bad one is asking
            // a question whose answer is "no revisions", not an error.
            if (!SourceId.TryParse(sourceId.Trim(), out SourceId filter))
            {
                return [];
            }

            query = query.Where(revision => revision.SourceId == filter);
        }

        return await Project(query
                .OrderByDescending(revision => revision.CreatedAtUtc)
                .ThenByDescending(revision => revision.Id)
                .Take(limit))
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

        ScheduleRevisionSummary summary = await Project(dbContext.ScheduleRevisions
                .AsNoTracking()
                .Where(candidate => candidate.Id == revisionId))
            .SingleAsync(cancellationToken);

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
                // A finding with no evidence stores SQL NULL; the read contract keeps its
                // non-null string, so no-evidence reads back as the empty string it always did.
                Detail = finding.Detail ?? string.Empty,
            })
            .ToListAsync(cancellationToken);

        return new ScheduleRevisionDetail
        {
            Summary = summary,
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

    /// <summary>
    /// Projects revisions onto what a reviewer needs to read: whose schedule this is, how it
    /// compares with what is live, and what validation found.
    /// </summary>
    /// <remarks>
    /// The published comparison deliberately excludes the revision itself, so a published revision
    /// is compared against the one it replaced rather than against its own record count, which
    /// would always read as "no change".
    /// </remarks>
    private IQueryable<ScheduleRevisionSummary> Project(IQueryable<ScheduleRevision> revisions) =>
        from revision in revisions
        join source in dbContext.ScheduleSources.AsNoTracking()
            on revision.ScheduleSourceId equals source.Id
        select new ScheduleRevisionSummary
        {
            RevisionId = revision.Id,
            SourceId = revision.SourceId.Value,
            DisplayName = source.DisplayName,
            ClassYear = source.ClassYear,
            ProgramLanguage = source.ProgramLanguage,
            AcademicYear = source.AcademicYear,
            State = revision.State,
            CreatedAtUtc = revision.CreatedAtUtc,
            RecordCount = revision.RecordCount,
            PublishedRecordCount = dbContext.ScheduleRevisions
                .Where(published => published.ScheduleSourceId == revision.ScheduleSourceId
                    && published.State == RevisionState.Published
                    && published.Id != revision.Id)
                .Select(published => (int?)published.RecordCount)
                .FirstOrDefault(),
            ErrorFindingCount = dbContext.RevisionValidationFindings.Count(
                finding => finding.ScheduleRevisionId == revision.Id
                    && finding.Severity == ValidationSeverity.Error),
            WarningFindingCount = dbContext.RevisionValidationFindings.Count(
                finding => finding.ScheduleRevisionId == revision.Id
                    && finding.Severity == ValidationSeverity.Warning),
            StateReason = revision.StateReason,
        };
}
