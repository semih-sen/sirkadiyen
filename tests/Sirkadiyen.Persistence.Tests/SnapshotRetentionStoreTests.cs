using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Parsing;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SnapshotRetentionStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RetentionKeepsTheActiveYearAnchorLatestAndRecentWindow()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = Source();
        context.ScheduleSources.Add(source);

        SourceSnapshot previousYear = Snapshot(
            source,
            "previous-year",
            "2024-2025",
            Now.AddDays(-40));
        SourceSnapshot anchor = Snapshot(
            source,
            "active-anchor",
            source.AcademicYear,
            Now.AddDays(-30));
        SourceSnapshot expired = Snapshot(
            source,
            "expired",
            source.AcademicYear,
            Now.AddDays(-15));
        SourceSnapshot recent = Snapshot(
            source,
            "recent",
            source.AcademicYear,
            Now.AddDays(-5));
        SourceSnapshot latest = Snapshot(
            source,
            "latest",
            source.AcademicYear,
            Now.AddDays(-1));

        SourceSnapshot[] snapshots = [previousYear, anchor, expired, recent, latest];
        context.SourceSnapshots.AddRange(snapshots);
        foreach (SourceSnapshot snapshot in snapshots)
        {
            context.ParseRuns.Add(CompletedRun(snapshot));
        }

        await context.SaveChangesAsync(Token);
        SnapshotRetentionStore store = new(context);

        IReadOnlyList<PrunedSnapshotPayload> pruned =
            await store.PruneExpiredPayloadsAsync(
                Now.AddDays(-10),
                Now,
                batchSize: 50,
                Token);

        Assert.Equal(
            [previousYear.Id, expired.Id],
            pruned.Select(result => result.SnapshotId));

        context.ChangeTracker.Clear();
        Dictionary<Guid, SourceSnapshot> stored = await context.SourceSnapshots
            .Where(snapshot => snapshots.Select(candidate => candidate.Id).Contains(snapshot.Id))
            .ToDictionaryAsync(snapshot => snapshot.Id, Token);

        Assert.Null(stored[previousYear.Id].Payload);
        Assert.Null(stored[expired.Id].Payload);
        Assert.NotNull(stored[anchor.Id].Payload);
        Assert.NotNull(stored[recent.Id].Payload);
        Assert.NotNull(stored[latest.Id].Payload);
    }

    [Fact]
    public async Task RetentionKeepsTheLatestPayloadAfterALongQuietPeriod()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = Source();
        context.ScheduleSources.Add(source);

        SourceSnapshot anchor = Snapshot(source, "anchor", source.AcademicYear, Now.AddDays(-40));
        SourceSnapshot expired = Snapshot(source, "expired", source.AcademicYear, Now.AddDays(-30));
        SourceSnapshot latest = Snapshot(source, "latest", source.AcademicYear, Now.AddDays(-20));
        context.SourceSnapshots.AddRange(anchor, expired, latest);
        context.ParseRuns.AddRange(
            CompletedRun(anchor),
            CompletedRun(expired),
            CompletedRun(latest));
        await context.SaveChangesAsync(Token);

        SnapshotRetentionStore store = new(context);
        IReadOnlyList<PrunedSnapshotPayload> pruned =
            await store.PruneExpiredPayloadsAsync(
                Now.AddDays(-10),
                Now,
                batchSize: 50,
                Token);

        Assert.Equal(expired.Id, Assert.Single(pruned).SnapshotId);
        context.ChangeTracker.Clear();
        Assert.NotNull((await context.SourceSnapshots.SingleAsync(
            snapshot => snapshot.Id == latest.Id,
            Token)).Payload);
    }

    [Fact]
    public async Task RetentionKeepsSnapshotsNeededByParserRecovery()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = Source();
        context.ScheduleSources.Add(source);

        SourceSnapshot anchor = Snapshot(source, "anchor", source.AcademicYear, Now.AddDays(-40));
        SourceSnapshot noRun = Snapshot(source, "no-run", source.AcademicYear, Now.AddDays(-30));
        SourceSnapshot running = Snapshot(source, "running", source.AcademicYear, Now.AddDays(-20));
        SourceSnapshot failed = Snapshot(source, "failed", source.AcademicYear, Now.AddDays(-15));
        SourceSnapshot latest = Snapshot(source, "latest", source.AcademicYear, Now.AddDays(-1));
        context.SourceSnapshots.AddRange(anchor, noRun, running, failed, latest);

        context.ParseRuns.Add(CompletedRun(anchor));
        context.ParseRuns.Add(new ParseRun(
            running.Id,
            source.ParserProfile,
            source.ParserProfileVersion,
            "running",
            running.AcquiredAtUtc));
        ParseRun failedRun = new(
            failed.Id,
            source.ParserProfile,
            source.ParserProfileVersion,
            "failed",
            failed.AcquiredAtUtc);
        failedRun.Fail(failed.AcquiredAtUtc.AddMinutes(1), "transport");
        context.ParseRuns.Add(failedRun);
        context.ParseRuns.Add(CompletedRun(latest));
        await context.SaveChangesAsync(Token);

        SnapshotRetentionStore store = new(context);
        IReadOnlyList<PrunedSnapshotPayload> pruned =
            await store.PruneExpiredPayloadsAsync(
                Now.AddDays(-10),
                Now,
                batchSize: 50,
                Token);

        Assert.Empty(pruned);
    }

    private static ScheduleSource Source() => new(
        SourceId.Parse($"G9-RET-{Guid.NewGuid():N}"[..20]),
        "Retention test source",
        ScheduleSourceTransport.GoogleSheets,
        ScheduleDocumentFormat.GoogleSheet,
        "https://example.invalid/sheet",
        "grade1_yearly_v1",
        "1.0.0",
        "2025-2026",
        1,
        ProgramLanguage.Turkish,
        "Europe/Istanbul");

    private static SourceSnapshot Snapshot(
        ScheduleSource source,
        string discriminator,
        string academicYear,
        DateTimeOffset acquiredAtUtc) => new(
            source.Id,
            source.SourceId,
            $"snapshot-{discriminator}",
            "spreadsheet-1",
            academicYear,
            acquiredAtUtc,
            $"sha256:{discriminator}",
            "1.0",
            "{}",
            1,
            1,
            0);

    private static ParseRun CompletedRun(SourceSnapshot snapshot)
    {
        ParseRun run = new(
            snapshot.Id,
            "grade1_yearly_v1",
            "1.0.0",
            $"run-{snapshot.ExternalSnapshotId}",
            snapshot.AcquiredAtUtc);
        run.Complete(
            ParseRunStatus.Completed,
            snapshot.AcquiredAtUtc.AddMinutes(1),
            "{}",
            1,
            0,
            0);
        return run;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
