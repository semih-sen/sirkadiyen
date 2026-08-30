using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.StudentRosters;

namespace Sirkadiyen.Infrastructure.Persistence.StudentRosters.Stores;

/// <summary>The append-only roster catalog history (ADR-134).</summary>
public sealed class StudentRosterCatalogRevisionStore(SirkadiyenDbContext dbContext)
    : IStudentRosterCatalogRevisionStore
{
    /// <summary>
    /// Records the revision, writing the pre-edit baseline first when the history is still empty.
    /// </summary>
    /// <remarks>
    /// One transaction, so the baseline and the edit it precedes cannot half-commit. The baseline
    /// is inserted here rather than by the caller so the "is the history empty" test and the
    /// insert cannot interleave with a second administrator's first edit.
    /// </remarks>
    public async Task CommitAsync(
        StudentRosterCatalogCommit commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);

        await RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (commit.Baseline is { } baseline
                && !await dbContext.StudentRosterCatalogRevisions.AnyAsync(cancellationToken))
            {
                dbContext.StudentRosterCatalogRevisions.Add(
                    StudentRosterCatalogRevision.Baseline(
                        baseline.RecordedAtUtc,
                        baseline.Content,
                        baseline.ContentHash,
                        baseline.RosterCount));
            }

            dbContext.StudentRosterCatalogRevisions.Add(commit.Revision);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task<IReadOnlyList<StudentRosterCatalogRevisionSummary>> ListAsync(
        int limit,
        string currentContentHash,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        List<StudentRosterCatalogRevision> rows = await dbContext.StudentRosterCatalogRevisions
            .AsNoTracking()
            .OrderByDescending(revision => revision.RecordedAtUtc)
            .ThenByDescending(revision => revision.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(revision => Summarize(revision, currentContentHash))];
    }

    public async Task<StudentRosterCatalogRevisionDetail?> FindAsync(
        Guid id,
        string currentContentHash,
        CancellationToken cancellationToken)
    {
        StudentRosterCatalogRevision? revision = await dbContext.StudentRosterCatalogRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return revision is null
            ? null
            : new StudentRosterCatalogRevisionDetail
            {
                Summary = Summarize(revision, currentContentHash),
                Content = revision.Content,
            };
    }

    private static StudentRosterCatalogRevisionSummary Summarize(
        StudentRosterCatalogRevision revision,
        string currentContentHash) => new()
        {
            Id = revision.Id,
            Kind = revision.Kind.ToString(),
            RecordedAtUtc = revision.RecordedAtUtc,
            ContentHash = revision.ContentHash,
            PreviousContentHash = revision.PreviousContentHash,
            RosterCount = revision.RosterCount,
            ActorUserId = revision.ActorUserId,
            ActorEmail = revision.ActorEmail,
            Reason = revision.Reason,
            ChangeSummary = revision.ChangeSummary,

            // Compared against what the file holds now, not against the newest row: a document
            // changed outside the panel must not be presented as the last confirmed revision.
            IsCurrent = string.Equals(
                revision.ContentHash,
                currentContentHash,
                StringComparison.Ordinal),
        };
}
