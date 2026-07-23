using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of the bounded snapshot-payload retention policy.
/// </summary>
public sealed class SnapshotRetentionStore(SirkadiyenDbContext dbContext)
    : ISnapshotRetentionStore
{
    public Task<IReadOnlyList<PrunedSnapshotPayload>> PruneExpiredPayloadsAsync(
        DateTimeOffset cutoffUtc,
        DateTimeOffset prunedAtUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (cutoffUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The retention cutoff must be expressed in UTC.",
                nameof(cutoffUtc));
        }

        if (prunedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The prune time must be expressed in UTC.",
                nameof(prunedAtUtc));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        return RetriableTransaction.ExecuteAsync<IReadOnlyList<PrunedSnapshotPayload>>(
            dbContext,
            async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                List<SourceSnapshot> candidates = await dbContext.SourceSnapshots
                    .Where(snapshot =>
                        snapshot.Payload != null
                        && snapshot.AcquiredAtUtc < cutoffUtc)
                    // Keep the newest content per source even after a quiet
                    // period. A parser-profile version change reparses this
                    // snapshot when the source itself is unchanged.
                    .Where(snapshot => dbContext.SourceSnapshots.Any(other =>
                        other.ScheduleSourceId == snapshot.ScheduleSourceId
                        && other.AcquiredAtUtc > snapshot.AcquiredAtUtc))
                    // Keep the first snapshot captured for the source's current
                    // academic year. It is the main baseline an operator uses
                    // when asking how the year began.
                    .Where(snapshot => !dbContext.ScheduleSources.Any(source =>
                        source.Id == snapshot.ScheduleSourceId
                        && source.AcademicYear == snapshot.AcademicYear
                        && !dbContext.SourceSnapshots.Any(earlier =>
                            earlier.ScheduleSourceId == snapshot.ScheduleSourceId
                            && earlier.AcademicYear == snapshot.AcademicYear
                            && earlier.AcquiredAtUtc < snapshot.AcquiredAtUtc)))
                    // A stored-during-freeze snapshot has no run yet, and a
                    // failed/running run still needs the original payload to
                    // resume. Only fully terminal parser evidence is eligible.
                    .Where(snapshot => dbContext.ParseRuns.Any(run =>
                        run.SourceSnapshotId == snapshot.Id))
                    .Where(snapshot => !dbContext.ParseRuns.Any(run =>
                        run.SourceSnapshotId == snapshot.Id
                        && (run.Status == ParseRunStatus.Running
                            || run.Status == ParseRunStatus.Failed)))
                    .OrderBy(snapshot => snapshot.AcquiredAtUtc)
                    .ThenBy(snapshot => snapshot.Id)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

                foreach (SourceSnapshot snapshot in candidates)
                {
                    snapshot.PrunePayload(prunedAtUtc);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return candidates
                    .Select(static snapshot => new PrunedSnapshotPayload
                    {
                        SnapshotId = snapshot.Id,
                        SourceId = snapshot.SourceId,
                        AcquiredAtUtc = snapshot.AcquiredAtUtc,
                    })
                    .ToArray();
            });
    }
}
