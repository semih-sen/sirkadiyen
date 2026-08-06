using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>
/// Serves the held-diff queue and records a release (ADR-042).
/// </summary>
/// <remarks>
/// Releasing is a single <c>SaveChanges</c>, which is already one transaction,
/// so this store does not open its own and does not need
/// <see cref="RetriableTransaction"/>. Concurrency is handled by the diff's row
/// version rather than by locking.
/// </remarks>
public sealed class ScheduleDiffReviewStore(SirkadiyenDbContext dbContext) : IScheduleDiffReviewStore
{
    public async Task<IReadOnlyList<ScheduleDiffSummary>> ListByStateAsync(
        ScheduleDiffState state,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<ScheduleDiff> diffs = await dbContext.ScheduleDiffs
            .AsNoTracking()
            .Where(diff => diff.State == state)
            .OrderBy(diff => diff.CreatedAtUtc)
            .ThenBy(diff => diff.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. diffs.Select(Summarize)];
    }

    public async Task<IReadOnlyList<ScheduleDiffSummary>> ListByDispatchStateAsync(
        CalendarDispatchState dispatchState,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<ScheduleDiff> diffs = await dbContext.ScheduleDiffs
            .AsNoTracking()
            .Where(diff => diff.CalendarDispatchState == dispatchState)
            .OrderBy(diff => diff.CreatedAtUtc)
            .ThenBy(diff => diff.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. diffs.Select(Summarize)];
    }

    public async Task<ScheduleDiffDetail?> FindAsync(
        Guid scheduleDiffId,
        int entryLimit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryLimit);

        ScheduleDiff? diff = await dbContext.ScheduleDiffs
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == scheduleDiffId, cancellationToken);
        if (diff is null)
        {
            return null;
        }

        IQueryable<ScheduleDiffEntry> actionable = dbContext.ScheduleDiffEntries
            .AsNoTracking()
            .Where(entry => entry.ScheduleDiffId == scheduleDiffId
                && entry.Change != ScheduleDiffChange.Unchanged);

        // Deletions first, then ambiguity: they are what the gate exists to
        // catch, and an operator who reads nothing else must still see them.
        // Unchanged entries are excluded entirely; they are the overwhelming
        // majority and say nothing about whether the hold is legitimate.
        List<ScheduleDiffEntry> entries = await actionable
            .OrderBy(entry => entry.Change == ScheduleDiffChange.Deleted
                ? 0
                : entry.Change == ScheduleDiffChange.Ambiguous
                    ? 1
                    : entry.Change == ScheduleDiffChange.Updated ? 2 : 3)
            .ThenBy(entry => entry.Id)
            .Take(entryLimit)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, ScheduleDiffRecordView> records = await LoadRecordsAsync(
            [.. entries.SelectMany(entry => new[] { entry.PreviousRecordId, entry.CurrentRecordId })
                .OfType<Guid>()
                .Distinct()],
            cancellationToken);

        return new ScheduleDiffDetail
        {
            Summary = Summarize(diff),
            ActionableEntryCount = await actionable.CountAsync(cancellationToken),
            Entries =
            [
                .. entries.Select(entry => new ScheduleDiffEntryView
                {
                    Change = entry.Change,
                    Match = entry.Match,
                    MatchScore = entry.MatchScore,
                    Previous = Look(records, entry.PreviousRecordId),
                    Current = Look(records, entry.CurrentRecordId),
                }),
            ],
        };
    }

    public async Task<ScheduleDiffReleaseResult> ReleaseAsync(
        Guid scheduleDiffId,
        string releasedBy,
        string releaseReason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ScheduleDiff? diff = await dbContext.ScheduleDiffs
            .SingleOrDefaultAsync(candidate => candidate.Id == scheduleDiffId, cancellationToken);

        if (diff is null)
        {
            return new ScheduleDiffReleaseResult
            {
                Outcome = ScheduleDiffReleaseOutcome.DiffNotFound,
            };
        }

        if (diff.State is not ScheduleDiffState.Held)
        {
            return new ScheduleDiffReleaseResult
            {
                Outcome = ScheduleDiffReleaseOutcome.NotHeld,
                ObservedState = diff.State,
            };
        }

        if (!diff.IsReleasable)
        {
            return new ScheduleDiffReleaseResult
            {
                Outcome = ScheduleDiffReleaseOutcome.AmbiguityMustBeResolvedAtSource,
                ObservedState = diff.State,
            };
        }

        diff.Release(releasedBy, releaseReason, atUtc);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed this diff between the read and the write. The
            // release is reported as refused rather than retried: a second
            // operator must see the current state before vouching for it.
            dbContext.ChangeTracker.Clear();
            return new ScheduleDiffReleaseResult
            {
                Outcome = ScheduleDiffReleaseOutcome.ConcurrentRelease,
            };
        }

        return new ScheduleDiffReleaseResult
        {
            Outcome = ScheduleDiffReleaseOutcome.Released,
            ObservedState = diff.State,
            ReleasedAtUtc = atUtc,
        };
    }

    public async Task<ScheduleDiffRetryResult> RetryDispatchAsync(
        Guid scheduleDiffId,
        string retriedBy,
        string retryReason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ScheduleDiff? diff = await dbContext.ScheduleDiffs
            .SingleOrDefaultAsync(candidate => candidate.Id == scheduleDiffId, cancellationToken);

        if (diff is null)
        {
            return new ScheduleDiffRetryResult
            {
                Outcome = ScheduleDiffRetryOutcome.DiffNotFound,
            };
        }

        if (!diff.IsDispatchable)
        {
            // A held diff may not reach a calendar at all, so retrying it would be releasing it
            // under another name. That decision is ADR-042's, and it is a different one.
            return new ScheduleDiffRetryResult
            {
                Outcome = ScheduleDiffRetryOutcome.NotDispatchable,
                ObservedDispatchState = diff.CalendarDispatchState,
            };
        }

        if (!diff.IsDispatchRetriable)
        {
            return new ScheduleDiffRetryResult
            {
                Outcome = ScheduleDiffRetryOutcome.NotFailed,
                ObservedDispatchState = diff.CalendarDispatchState,
            };
        }

        diff.RetryDispatch(retriedBy, retryReason, atUtc);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A worker or another operator changed this diff between the read and the write. The
            // retry is refused rather than reapplied: whoever retries it must see its current
            // state first, exactly as a release does.
            dbContext.ChangeTracker.Clear();
            return new ScheduleDiffRetryResult
            {
                Outcome = ScheduleDiffRetryOutcome.ConcurrentChange,
            };
        }

        return new ScheduleDiffRetryResult
        {
            Outcome = ScheduleDiffRetryOutcome.Retried,
            ObservedDispatchState = diff.CalendarDispatchState,
            RetriedAtUtc = atUtc,
            DispatchRetryCount = diff.DispatchRetryCount,
        };
    }

    private async Task<Dictionary<Guid, ScheduleDiffRecordView>> LoadRecordsAsync(
        Guid[] recordIds,
        CancellationToken cancellationToken)
    {
        if (recordIds.Length is 0)
        {
            return [];
        }

        List<CanonicalScheduleRecord> records = await dbContext.CanonicalScheduleRecords
            .AsNoTracking()
            .Where(record => recordIds.Contains(record.Id))
            .ToListAsync(cancellationToken);

        return records.ToDictionary(
            record => record.Id,
            record => new ScheduleDiffRecordView
            {
                RecordId = record.Id,
                DisplayTitle = record.DisplayTitle,
                LocalDate = record.LocalDate,
                StartLocalTime = record.StartLocalTime,
                EndLocalTime = record.EndLocalTime,
                IsAllDay = record.IsAllDay,
                AudienceSelectors = record.AudienceSelectors,
                Instructor = record.Instructor,
                Location = record.Location,
            });
    }

    private static ScheduleDiffRecordView? Look(
        Dictionary<Guid, ScheduleDiffRecordView> records,
        Guid? recordId) =>
        recordId is { } id && records.TryGetValue(id, out ScheduleDiffRecordView? record)
            ? record
            : null;

    private static ScheduleDiffSummary Summarize(ScheduleDiff diff) => new()
    {
        ScheduleDiffId = diff.Id,
        SourceId = diff.SourceId.Value,
        State = diff.State,
        CurrentRevisionId = diff.CurrentRevisionId,
        PreviousRevisionId = diff.PreviousRevisionId,
        CreatedCount = diff.CreatedCount,
        UpdatedCount = diff.UpdatedCount,
        DeletedCount = diff.DeletedCount,
        UnchangedCount = diff.UnchangedCount,
        AmbiguousCount = diff.AmbiguousCount,
        PreviousRecordCount = diff.PreviousRecordCount,
        CurrentRecordCount = diff.CurrentRecordCount,
        CreatedAtUtc = diff.CreatedAtUtc,
        HoldReason = diff.HoldReason,
        IsReleasable = diff.IsReleasable,
        ReleasedBy = diff.ReleasedBy,
        ReleaseReason = diff.ReleaseReason,
        ReleasedAtUtc = diff.ReleasedAtUtc,
        CalendarDispatchState = diff.CalendarDispatchState,
        DispatchAttempts = diff.DispatchAttempts,
        DispatchedAtUtc = diff.DispatchedAtUtc,
        DispatchFailureReason = diff.DispatchFailureReason,
        IsDispatchRetriable = diff.IsDispatchRetriable,
        DispatchRetryCount = diff.DispatchRetryCount,
        LastDispatchRetriedBy = diff.LastDispatchRetriedBy,
        LastDispatchRetryReason = diff.LastDispatchRetryReason,
        LastDispatchRetriedAtUtc = diff.LastDispatchRetriedAtUtc,
    };
}
