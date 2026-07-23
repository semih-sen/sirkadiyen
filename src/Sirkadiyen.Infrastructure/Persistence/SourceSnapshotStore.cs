using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>
/// Stores snapshots in PostgreSQL and implements the unchanged-source short
/// circuit.
/// </summary>
public sealed class SourceSnapshotStore(SirkadiyenDbContext dbContext) : ISourceSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    public Task<StoreSnapshotResult> StoreIfChangedAsync(
        SourceId sourceId,
        NormalizedSpreadsheetSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return RetriableTransaction.ExecuteAsync(
            dbContext,
            () => StoreIfChangedCoreAsync(sourceId, snapshot, cancellationToken));
    }

    private async Task<StoreSnapshotResult> StoreIfChangedCoreAsync(
        SourceId sourceId,
        NormalizedSpreadsheetSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        // The comparison and the insert have to be one atomic decision.
        // Two workers polling the same source concurrently would otherwise both
        // see "changed" and store the same content twice, which would start two
        // parse runs and two diffs for one change.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        // xmin is selected explicitly because it backs the concurrency token and
        // PostgreSQL does not include system columns in SELECT *.
        ScheduleSource source = await dbContext.ScheduleSources
            .FromSql($"""
                SELECT *, xmin FROM sirkadiyen.schedule_sources
                WHERE "SourceId" = {sourceId.Value}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Source '{sourceId}' is not configured, so its snapshot cannot be stored.");

        SourceSnapshot? latest = await LatestQuery(sourceId).FirstOrDefaultAsync(cancellationToken);

        if (latest is not null && string.Equals(
            latest.ContentHash,
            snapshot.ContentHash,
            StringComparison.Ordinal))
        {
            source.RecordPolled(snapshot.AcquiredAtUtc, changed: false);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new StoreSnapshotResult
            {
                Outcome = StoreSnapshotOutcome.Unchanged,
                Snapshot = latest,
            };
        }

        SourceSnapshot stored = new(
            source.Id,
            sourceId,
            snapshot.SnapshotId,
            snapshot.SpreadsheetId,
            source.AcademicYear,
            snapshot.AcquiredAtUtc,
            snapshot.ContentHash,
            snapshot.ContractVersion,
            JsonSerializer.Serialize(snapshot, JsonOptions),
            snapshot.Worksheets.Count,
            snapshot.Worksheets.Sum(static worksheet => worksheet.Cells.Count),
            snapshot.Diagnostics.Count);

        dbContext.SourceSnapshots.Add(stored);
        source.RecordPolled(snapshot.AcquiredAtUtc, changed: true);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new StoreSnapshotResult
        {
            Outcome = StoreSnapshotOutcome.Stored,
            Snapshot = stored,
        };
    }

    public Task<SourceSnapshot?> GetLatestAsync(
        SourceId sourceId,
        CancellationToken cancellationToken) =>
        LatestQuery(sourceId).FirstOrDefaultAsync(cancellationToken);

    private IQueryable<SourceSnapshot> LatestQuery(SourceId sourceId) =>
        dbContext.SourceSnapshots
            .Where(snapshot => snapshot.SourceId == sourceId)
            .OrderByDescending(snapshot => snapshot.AcquiredAtUtc)
            .ThenByDescending(snapshot => snapshot.Id);
}
