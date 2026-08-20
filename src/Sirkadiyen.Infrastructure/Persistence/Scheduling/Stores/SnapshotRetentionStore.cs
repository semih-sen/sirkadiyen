using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

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
                    // A scoped freeze protects only its own class/program evidence while
                    // allowing retention to continue for every other pipeline.
                    .Where(snapshot => !dbContext.ScopedOperationalFreezeControls.Any(control =>
                        control.IsFrozen
                        && dbContext.ScheduleSources.Any(source =>
                            source.Id == snapshot.ScheduleSourceId
                            && source.ClassYear == control.ClassYear
                            && source.ProgramLanguage == control.ProgramLanguage)))
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

    public async Task<SnapshotPruneCandidate?> FindPruneCandidateAsync(
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        // Projected without the payload column: the large jsonb never leaves the database just to
        // decide eligibility. HasPayload is the IS NOT NULL test PostgreSQL runs in place.
        var row = await (
                from snapshot in dbContext.SourceSnapshots.AsNoTracking()
                join source in dbContext.ScheduleSources.AsNoTracking()
                    on snapshot.ScheduleSourceId equals source.Id
                where snapshot.Id == snapshotId
                select new
                {
                    snapshot.Id,
                    snapshot.SourceId,
                    snapshot.ScheduleSourceId,
                    snapshot.AcquiredAtUtc,
                    SnapshotAcademicYear = snapshot.AcademicYear,
                    HasPayload = snapshot.Payload != null,
                    source.ClassYear,
                    source.ProgramLanguage,
                    SourceAcademicYear = source.AcademicYear,
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var scope = new OperationalFreezeScope
        {
            ClassYear = row.ClassYear,
            ProgramLanguage = row.ProgramLanguage,
        };

        string? ineligibleReason = row.HasPayload
            ? await EvaluateManualPruneIneligibilityAsync(
                row.Id,
                row.ScheduleSourceId,
                row.AcquiredAtUtc,
                row.SnapshotAcademicYear,
                row.SourceAcademicYear,
                cancellationToken)
            : null;

        return new SnapshotPruneCandidate
        {
            SnapshotId = row.Id,
            SourceId = row.SourceId,
            AcquiredAtUtc = row.AcquiredAtUtc,
            Scope = scope,
            PayloadAlreadyPruned = !row.HasPayload,
            IneligibleReason = ineligibleReason,
        };
    }

    /// <summary>
    /// Applies the same recovery and baseline guards as <see cref="PruneExpiredPayloadsAsync"/> to a
    /// single snapshot, returning the first reason it may not be pruned or <see langword="null"/>
    /// when it is eligible. The batch's recent-time window is deliberately not applied here: a manual
    /// prune is authorised by an operator who has decided the snapshot is old enough.
    /// </summary>
    private async Task<string?> EvaluateManualPruneIneligibilityAsync(
        Guid snapshotId,
        Guid scheduleSourceId,
        DateTimeOffset acquiredAtUtc,
        string snapshotAcademicYear,
        string sourceAcademicYear,
        CancellationToken cancellationToken)
    {
        bool hasAnyRun = await dbContext.ParseRuns
            .AnyAsync(run => run.SourceSnapshotId == snapshotId, cancellationToken);
        if (!hasAnyRun)
        {
            return "The snapshot has not been parsed yet and is still primary evidence.";
        }

        bool hasOpenOrFailedRun = await dbContext.ParseRuns.AnyAsync(
            run => run.SourceSnapshotId == snapshotId
                && (run.Status == ParseRunStatus.Running || run.Status == ParseRunStatus.Failed),
            cancellationToken);
        if (hasOpenOrFailedRun)
        {
            return "A parse run still needs the payload to recover; only terminal, successful "
                + "parser evidence may be pruned.";
        }

        bool hasNewer = await dbContext.SourceSnapshots.AnyAsync(
            other => other.ScheduleSourceId == scheduleSourceId
                && other.AcquiredAtUtc > acquiredAtUtc,
            cancellationToken);
        if (!hasNewer)
        {
            return "This is the newest snapshot for its source; it is retained so a parser-profile "
                + "change can reparse it.";
        }

        // The first snapshot of the source's current academic year is the baseline an operator uses
        // to ask how the year began, so it is kept even after a long quiet period.
        if (sourceAcademicYear == snapshotAcademicYear)
        {
            bool hasEarlierSameYear = await dbContext.SourceSnapshots.AnyAsync(
                earlier => earlier.ScheduleSourceId == scheduleSourceId
                    && earlier.AcademicYear == snapshotAcademicYear
                    && earlier.AcquiredAtUtc < acquiredAtUtc,
                cancellationToken);
            if (!hasEarlierSameYear)
            {
                return "This is the first snapshot of the source's current academic year; it is "
                    + "retained as the year's baseline.";
            }
        }

        return null;
    }

    public Task<bool> PrunePayloadAsync(
        Guid snapshotId,
        DateTimeOffset prunedAtUtc,
        CancellationToken cancellationToken)
    {
        if (prunedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The prune time must be expressed in UTC.",
                nameof(prunedAtUtc));
        }

        return RetriableTransaction.ExecuteAsync<bool>(
            dbContext,
            async () =>
            {
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                SourceSnapshot? snapshot = await dbContext.SourceSnapshots
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == snapshotId,
                        cancellationToken);

                if (snapshot is null || snapshot.Payload is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }

                snapshot.PrunePayload(prunedAtUtc);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            });
    }
}
